using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.WorkflowEngine;

namespace OfficeTaskManagement.Services.Onboarding
{
    /// <inheritdoc />
    public class OnboardingOrchestrationService : IOnboardingOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeminiAiService _ai;
        private readonly IWorkflowEngineService _workflowEngine;
        private readonly ILogger<OnboardingOrchestrationService> _logger;

        public OnboardingOrchestrationService(
            ApplicationDbContext db,
            IGeminiAiService ai,
            IWorkflowEngineService workflowEngine,
            ILogger<OnboardingOrchestrationService> logger)
        {
            _db            = db;
            _ai            = ai;
            _workflowEngine = workflowEngine;
            _logger        = logger;
        }

        // ── Step 2: Analyze project ───────────────────────────────────────────

        public async Task<ProjectAnalysisResult> AnalyzeProjectAsync(int projectId, CancellationToken ct = default)
        {
            _logger.LogInformation("Analyzing project codebase for onboarding. ProjectId={ProjectId}", projectId);
            return await _ai.AnalyzeProjectCodebaseAsync(projectId, ct);
        }

        // ── Step 3: Analyze features for a single epic ────────────────────────

        public async Task<FeatureAnalysisResult> AnalyzeFeaturesForEpicAsync(int epicId, CancellationToken ct = default)
        {
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == epicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            string? userId = null; // analysis-only; set null to avoid FK violation

            var featuresDto = await _ai.SuggestFeaturesForEpicAsync(project.Id, epic.Name, epic.Description ?? "", ct);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await _db.Features.IgnoreQueryFilters()
                    .Where(f => f.EpicId == epicId).ToListAsync(ct);
                var featureIds = existing.Select(f => f.Id).ToList();

                var storiesToDelete = await _db.UserStories.IgnoreQueryFilters()
                    .Where(s => featureIds.Contains(s.FeatureId)).ToListAsync(ct);
                var storyIds = storiesToDelete.Select(s => s.Id).ToList();

                var tasksToDelete = await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => (t.UserStoryId.HasValue && storyIds.Contains(t.UserStoryId.Value)) || (t.FeatureId.HasValue && featureIds.Contains(t.FeatureId.Value))).ToListAsync(ct);
                _db.Tasks.RemoveRange(tasksToDelete);

                var testCasesToDelete = await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => storyIds.Contains(tc.UserStoryId)).ToListAsync(ct);
                _db.TestCases.RemoveRange(testCasesToDelete);

                await _db.SaveChangesAsync(ct);

                _db.UserStories.RemoveRange(storiesToDelete);
                await _db.SaveChangesAsync(ct);

                _db.Features.RemoveRange(existing);
                await _db.SaveChangesAsync(ct);

                var now    = DateTime.UtcNow;
                var result = new List<object>();
                foreach (var f in featuresDto)
                {
                    var feat = new Feature
                    {
                        EpicId      = epic.Id,
                        Name        = Truncate(f.Name, 200),
                        Description = f.Description,
                        CreatedById = userId,
                        CreatedAt   = now,
                        TenantId    = project.TenantId
                    };
                    _db.Features.Add(feat);
                    await _db.SaveChangesAsync(ct);
                    result.Add(MapFeature(feat));
                }

                await tx.CommitAsync(ct);
                return new FeatureAnalysisResult(epic.Id, epic.Name, result.ToArray());
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ── Step 4: Analyze stories for a single feature ──────────────────────

        public async Task<StoryAnalysisResult> AnalyzeStoriesForFeatureAsync(int featureId, CancellationToken ct = default)
        {
            var feat    = await RequireAsync(_db.Features.IgnoreQueryFilters(), f => f.Id == featureId, ct);
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == feat.EpicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            string? userId = null;

            var storiesDto = await _ai.SuggestUserStoriesForFeatureAsync(project.Id, epic.Name, feat.Name, feat.Description ?? "", ct);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await _db.UserStories.IgnoreQueryFilters()
                    .Where(s => s.FeatureId == featureId).ToListAsync(ct);
                var storyIds = existing.Select(s => s.Id).ToList();

                var tasksToDelete = await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => t.UserStoryId.HasValue && storyIds.Contains(t.UserStoryId.Value)).ToListAsync(ct);
                _db.Tasks.RemoveRange(tasksToDelete);

                var testCasesToDelete = await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => storyIds.Contains(tc.UserStoryId)).ToListAsync(ct);
                _db.TestCases.RemoveRange(testCasesToDelete);

                await _db.SaveChangesAsync(ct);

                _db.UserStories.RemoveRange(existing);
                await _db.SaveChangesAsync(ct);

                var now    = DateTime.UtcNow;
                var result = new List<object>();
                foreach (var s in storiesDto)
                {
                    var story = new UserStory
                    {
                        FeatureId          = feat.Id,
                        Title              = Truncate(s.Title, 200),
                        Description        = s.Description,
                        AcceptanceCriteria = s.AcceptanceCriteria,
                        Priority           = ParsePriority(s.Priority),
                        CreatedById        = userId,
                        CreatedAt          = now,
                        TenantId           = project.TenantId
                    };
                    _db.UserStories.Add(story);
                    await _db.SaveChangesAsync(ct);
                    result.Add(MapStory(story));
                }

                await tx.CommitAsync(ct);
                return new StoryAnalysisResult(feat.Id, feat.Name, result.ToArray());
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ── Step 5: Analyze tasks + test cases for a single story ─────────────

        public async Task<TasksAndTestsAnalysisResult> AnalyzeTasksAndTestsForStoryAsync(int storyId, CancellationToken ct = default)
        {
            var story   = await RequireAsync(_db.UserStories.IgnoreQueryFilters(), s => s.Id == storyId, ct);
            var feat    = await RequireAsync(_db.Features.IgnoreQueryFilters(), f => f.Id == story.FeatureId, ct);
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == feat.EpicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            string? userId = null;

            var resultDto = await _ai.SuggestTasksAndTestCasesAsync(project.Id, story.Title, story.Description ?? "", true, ct);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Clear existing
                _db.Tasks.RemoveRange(await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => t.UserStoryId == storyId).ToListAsync(ct));
                _db.TestCases.RemoveRange(await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => tc.UserStoryId == storyId).ToListAsync(ct));
                await _db.SaveChangesAsync(ct);

                var now       = DateTime.UtcNow;
                var taskList  = new List<object>();
                var testList  = new List<object>();

                foreach (var t in resultDto.Tasks)
                {
                    decimal pert = _workflowEngine.CalculatePert(t.OptimisticHours, t.MostLikelyHours, t.PessimisticHours);
                    var task = new TaskItem
                    {
                        UserStoryId              = story.Id,
                        ProjectId                = project.Id,
                        EpicId                   = epic.Id,
                        FeatureId                = feat.Id,
                        Title                    = Truncate(t.Title, 200),
                        Description              = t.Description,
                        Priority                 = ParsePriority(t.Priority),
                        EstimatedOptimisticHours  = t.OptimisticHours  > 0 ? t.OptimisticHours  : null,
                        EstimatedMostLikelyHours  = t.MostLikelyHours  > 0 ? t.MostLikelyHours  : null,
                        EstimatedPessimisticHours = t.PessimisticHours > 0 ? t.PessimisticHours : null,
                        PertEstimatedHours        = pert > 0 ? pert : null,
                        EstimatedHours            = pert > 0 ? pert : t.MostLikelyHours,
                        Status                   = Models.Enums.TaskStatus.New,
                        CreatedById              = userId,
                        CreatedAt                = now,
                        TenantId                 = project.TenantId
                    };
                    _db.Tasks.Add(task);
                    await _db.SaveChangesAsync(ct);
                    taskList.Add(MapTask(task));
                }

                foreach (var tcDto in resultDto.TestCases)
                {
                    var tc = new TestCase
                    {
                        UserStoryId    = story.Id,
                        Title          = Truncate(tcDto.Title, 200),
                        Steps          = tcDto.Steps,
                        ExpectedResult = tcDto.ExpectedResult,
                        IsAutomated    = false,
                        IsPassed       = false,
                        TenantId       = project.TenantId
                    };
                    _db.TestCases.Add(tc);
                    await _db.SaveChangesAsync(ct);
                    testList.Add(MapTestCase(tc));
                }

                await tx.CommitAsync(ct);
                return new TasksAndTestsAnalysisResult(story.Id, story.Title, taskList.ToArray(), testList.ToArray());
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ── Save operations (user-confirmed data) ─────────────────────────────

        public async Task<SaveEpicsResponse> SaveEpicsAsync(SaveEpicsRequest request, string userId, CancellationToken ct = default)
        {
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == request.ProjectId, ct);
            var now     = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await _db.Epics.IgnoreQueryFilters()
                    .Where(e => e.ProjectId == project.Id).ToListAsync(ct);

                var requestedIds = request.Epics.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).ToHashSet();
                var epicsToDeletes = existing.Where(e => !requestedIds.Contains(e.Id)).ToList();
                var epicIdsToDelete = epicsToDeletes.Select(e => e.Id).ToList();

                var featuresToDelete = await _db.Features.IgnoreQueryFilters()
                    .Where(f => epicIdsToDelete.Contains(f.EpicId)).ToListAsync(ct);
                var featureIdsToDelete = featuresToDelete.Select(f => f.Id).ToList();

                var storiesToDelete = await _db.UserStories.IgnoreQueryFilters()
                    .Where(s => featureIdsToDelete.Contains(s.FeatureId)).ToListAsync(ct);
                var storyIdsToDelete = storiesToDelete.Select(s => s.Id).ToList();

                var tasksToDelete = await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => (t.UserStoryId.HasValue && storyIdsToDelete.Contains(t.UserStoryId.Value)) || (t.EpicId.HasValue && epicIdsToDelete.Contains(t.EpicId.Value))).ToListAsync(ct);
                _db.Tasks.RemoveRange(tasksToDelete);

                var testCasesToDelete = await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => storyIdsToDelete.Contains(tc.UserStoryId)).ToListAsync(ct);
                _db.TestCases.RemoveRange(testCasesToDelete);

                await _db.SaveChangesAsync(ct);

                _db.UserStories.RemoveRange(storiesToDelete);
                await _db.SaveChangesAsync(ct);

                _db.Features.RemoveRange(featuresToDelete);
                await _db.SaveChangesAsync(ct);

                _db.Epics.RemoveRange(epicsToDeletes);
                await _db.SaveChangesAsync(ct);

                var saved = new List<object>();
                foreach (var reqEpic in request.Epics)
                {
                    Epic epic;
                    if (reqEpic.Id.HasValue)
                    {
                        epic = existing.First(e => e.Id == reqEpic.Id.Value);
                        epic.Name = Truncate(reqEpic.Name, 200);
                        epic.Description = reqEpic.Description;
                    }
                    else
                    {
                        epic = new Epic
                        {
                            ProjectId   = project.Id,
                            Name        = Truncate(reqEpic.Name, 200),
                            Description = reqEpic.Description,
                            CreatedById = userId,
                            CreatedAt   = now,
                            TenantId    = project.TenantId
                        };
                        _db.Epics.Add(epic);
                    }
                    await _db.SaveChangesAsync(ct);
                    saved.Add(new { id = epic.Id, name = epic.Name, description = epic.Description,
                                    selected = true, features = Array.Empty<object>() });
                }

                await tx.CommitAsync(ct);
                return new SaveEpicsResponse(saved.ToArray());
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        public async Task<SaveFeaturesResponse> SaveFeaturesAsync(SaveFeaturesRequest request, string userId, CancellationToken ct = default)
        {
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == request.EpicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            var now     = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing     = await _db.Features.IgnoreQueryFilters().Where(f => f.EpicId == epic.Id).ToListAsync(ct);
                var requestedIds = request.Features.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();
                var featuresToDelete = existing.Where(f => !requestedIds.Contains(f.Id)).ToList();
                var featureIdsToDelete = featuresToDelete.Select(f => f.Id).ToList();

                var storiesToDelete = await _db.UserStories.IgnoreQueryFilters()
                    .Where(s => featureIdsToDelete.Contains(s.FeatureId)).ToListAsync(ct);
                var storyIdsToDelete = storiesToDelete.Select(s => s.Id).ToList();

                var tasksToDelete = await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => (t.UserStoryId.HasValue && storyIdsToDelete.Contains(t.UserStoryId.Value)) || (t.FeatureId.HasValue && featureIdsToDelete.Contains(t.FeatureId.Value))).ToListAsync(ct);
                _db.Tasks.RemoveRange(tasksToDelete);

                var testCasesToDelete = await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => storyIdsToDelete.Contains(tc.UserStoryId)).ToListAsync(ct);
                _db.TestCases.RemoveRange(testCasesToDelete);

                await _db.SaveChangesAsync(ct);

                _db.UserStories.RemoveRange(storiesToDelete);
                await _db.SaveChangesAsync(ct);

                _db.Features.RemoveRange(featuresToDelete);
                await _db.SaveChangesAsync(ct);

                var saved = new List<object>();
                foreach (var reqFeat in request.Features)
                {
                    Feature feat;
                    if (reqFeat.Id.HasValue)
                    {
                        feat = existing.First(f => f.Id == reqFeat.Id.Value);
                        feat.Name = Truncate(reqFeat.Name, 200);
                        feat.Description = reqFeat.Description;
                    }
                    else
                    {
                        feat = new Feature
                        {
                            EpicId      = epic.Id,
                            Name        = Truncate(reqFeat.Name, 200),
                            Description = reqFeat.Description,
                            CreatedById = userId,
                            CreatedAt   = now,
                            TenantId    = project.TenantId
                        };
                        _db.Features.Add(feat);
                    }
                    await _db.SaveChangesAsync(ct);
                    saved.Add(MapFeature(feat));
                }

                await tx.CommitAsync(ct);
                return new SaveFeaturesResponse(epic.Id, saved.ToArray());
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        public async Task<SaveStoriesResponse> SaveStoriesAsync(SaveStoriesRequest request, string userId, CancellationToken ct = default)
        {
            var feat    = await RequireAsync(_db.Features.IgnoreQueryFilters(), f => f.Id == request.FeatureId, ct);
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == feat.EpicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            var now     = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing     = await _db.UserStories.IgnoreQueryFilters().Where(s => s.FeatureId == feat.Id).ToListAsync(ct);
                var requestedIds = request.Stories.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
                var storiesToDelete = existing.Where(s => !requestedIds.Contains(s.Id)).ToList();
                var storyIdsToDelete = storiesToDelete.Select(s => s.Id).ToList();

                var tasksToDelete = await _db.Tasks.IgnoreQueryFilters()
                    .Where(t => t.UserStoryId.HasValue && storyIdsToDelete.Contains(t.UserStoryId.Value)).ToListAsync(ct);
                _db.Tasks.RemoveRange(tasksToDelete);

                var testCasesToDelete = await _db.TestCases.IgnoreQueryFilters()
                    .Where(tc => storyIdsToDelete.Contains(tc.UserStoryId)).ToListAsync(ct);
                _db.TestCases.RemoveRange(testCasesToDelete);

                await _db.SaveChangesAsync(ct);

                _db.UserStories.RemoveRange(storiesToDelete);
                await _db.SaveChangesAsync(ct);

                var saved = new List<object>();
                foreach (var reqStory in request.Stories)
                {
                    UserStory story;
                    if (reqStory.Id.HasValue)
                    {
                        story = existing.First(s => s.Id == reqStory.Id.Value);
                        story.Title = Truncate(reqStory.Title, 200);
                        story.Description = reqStory.Description;
                        story.AcceptanceCriteria = reqStory.AcceptanceCriteria;
                        story.Priority = ParsePriority(reqStory.Priority);
                    }
                    else
                    {
                        story = new UserStory
                        {
                            FeatureId          = feat.Id,
                            Title              = Truncate(reqStory.Title, 200),
                            Description        = reqStory.Description,
                            AcceptanceCriteria = reqStory.AcceptanceCriteria,
                            Priority           = ParsePriority(reqStory.Priority),
                            CreatedById        = userId,
                            CreatedAt          = now,
                            TenantId           = project.TenantId
                        };
                        _db.UserStories.Add(story);
                    }
                    await _db.SaveChangesAsync(ct);
                    saved.Add(MapStory(story));
                }

                await tx.CommitAsync(ct);
                return new SaveStoriesResponse(feat.Id, saved.ToArray());
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        public async Task<SaveTasksResponse> SaveTasksAndTestsAsync(SaveTasksAndTestsRequest request, string userId, CancellationToken ct = default)
        {
            var story   = await RequireAsync(_db.UserStories.IgnoreQueryFilters(), s => s.Id == request.StoryId, ct);
            var feat    = await RequireAsync(_db.Features.IgnoreQueryFilters(), f => f.Id == story.FeatureId, ct);
            var epic    = await RequireAsync(_db.Epics.IgnoreQueryFilters(), e => e.Id == feat.EpicId, ct);
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == epic.ProjectId, ct);
            var now     = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Tasks
                var existingTasks = await _db.Tasks.IgnoreQueryFilters().Where(t => t.UserStoryId == story.Id).ToListAsync(ct);
                var reqTaskIds    = request.Tasks.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToHashSet();
                _db.Tasks.RemoveRange(existingTasks.Where(t => !reqTaskIds.Contains(t.Id)));

                var taskList = new List<object>();
                foreach (var reqTask in request.Tasks)
                {
                    decimal pert = _workflowEngine.CalculatePert(reqTask.OptimisticHours, reqTask.MostLikelyHours, reqTask.PessimisticHours);
                    TaskItem task;
                    if (reqTask.Id.HasValue)
                    {
                        task = existingTasks.First(t => t.Id == reqTask.Id.Value);
                        task.Title = Truncate(reqTask.Title, 200);
                        task.Description = reqTask.Description;
                        task.Priority = ParsePriority(reqTask.Priority);
                        task.EstimatedOptimisticHours  = reqTask.OptimisticHours  > 0 ? reqTask.OptimisticHours  : null;
                        task.EstimatedMostLikelyHours  = reqTask.MostLikelyHours  > 0 ? reqTask.MostLikelyHours  : null;
                        task.EstimatedPessimisticHours = reqTask.PessimisticHours > 0 ? reqTask.PessimisticHours : null;
                        task.PertEstimatedHours        = pert > 0 ? pert : null;
                        task.EstimatedHours            = pert > 0 ? pert : reqTask.MostLikelyHours;
                    }
                    else
                    {
                        task = new TaskItem
                        {
                            UserStoryId              = story.Id,
                            ProjectId                = project.Id,
                            EpicId                   = epic.Id,
                            FeatureId                = feat.Id,
                            Title                    = Truncate(reqTask.Title, 200),
                            Description              = reqTask.Description,
                            Priority                 = ParsePriority(reqTask.Priority),
                            EstimatedOptimisticHours  = reqTask.OptimisticHours  > 0 ? reqTask.OptimisticHours  : null,
                            EstimatedMostLikelyHours  = reqTask.MostLikelyHours  > 0 ? reqTask.MostLikelyHours  : null,
                            EstimatedPessimisticHours = reqTask.PessimisticHours > 0 ? reqTask.PessimisticHours : null,
                            PertEstimatedHours        = pert > 0 ? pert : null,
                            EstimatedHours            = pert > 0 ? pert : reqTask.MostLikelyHours,
                            Status                   = Models.Enums.TaskStatus.New,
                            CreatedById              = userId,
                            CreatedAt                = now,
                            TenantId                 = project.TenantId
                        };
                        _db.Tasks.Add(task);
                    }
                    await _db.SaveChangesAsync(ct);
                    taskList.Add(MapTask(task));
                }

                // Test Cases
                var existingTests = await _db.TestCases.IgnoreQueryFilters().Where(tc => tc.UserStoryId == story.Id).ToListAsync(ct);
                var reqTestIds    = request.TestCases.Where(tc => tc.Id.HasValue).Select(tc => tc.Id!.Value).ToHashSet();
                _db.TestCases.RemoveRange(existingTests.Where(tc => !reqTestIds.Contains(tc.Id)));

                var testList = new List<object>();
                foreach (var reqTest in request.TestCases)
                {
                    TestCase tc;
                    if (reqTest.Id.HasValue)
                    {
                        tc = existingTests.First(t => t.Id == reqTest.Id.Value);
                        tc.Title = Truncate(reqTest.Title, 200);
                        tc.Steps = reqTest.Steps;
                        tc.ExpectedResult = reqTest.ExpectedResult;
                    }
                    else
                    {
                        tc = new TestCase
                        {
                            UserStoryId    = story.Id,
                            Title          = Truncate(reqTest.Title, 200),
                            Steps          = reqTest.Steps,
                            ExpectedResult = reqTest.ExpectedResult,
                            IsAutomated    = false,
                            IsPassed       = false,
                            TenantId       = project.TenantId
                        };
                        _db.TestCases.Add(tc);
                    }
                    await _db.SaveChangesAsync(ct);
                    testList.Add(MapTestCase(tc));
                }

                await tx.CommitAsync(ct);
                return new SaveTasksResponse(story.Id, taskList.ToArray(), testList.ToArray());
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        public async Task CompleteOnboardingAsync(int projectId, string userId, CancellationToken ct = default)
        {
            var project = await RequireAsync(_db.Projects.IgnoreQueryFilters(), p => p.Id == projectId, ct);
            project.StrategicStatus           = ProjectStrategicStatus.Active;
            project.StrategicStatusChangedAt  = DateTime.UtcNow;
            project.StrategicStatusChangedById = userId;
            project.StrategicStatusReason     = "Initiated step-by-step via codebase-first onboarding wizard.";
            _db.Projects.Update(project);
            await _db.SaveChangesAsync(ct);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static async Task<T> RequireAsync<T>(IQueryable<T> set, System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct)
            where T : class
        {
            var entity = await set.FirstOrDefaultAsync(predicate, ct)
                ?? throw new InvalidOperationException($"{typeof(T).Name} not found.");
            return entity;
        }

        private static TaskPriority ParsePriority(string? value) =>
            Enum.TryParse<TaskPriority>(value, true, out var p) ? p : TaskPriority.Medium;

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        private static object MapFeature(Feature f) => new
        {
            id = f.Id, name = f.Name, description = f.Description,
            selected = true, userStories = Array.Empty<object>()
        };

        private static object MapStory(UserStory s) => new
        {
            id = s.Id, title = s.Title, description = s.Description,
            acceptanceCriteria = s.AcceptanceCriteria, priority = s.Priority.ToString(),
            selected = true, tasks = Array.Empty<object>(), testCases = Array.Empty<object>()
        };

        private static object MapTask(TaskItem t) => new
        {
            id = t.Id, title = t.Title, description = t.Description,
            priority = t.Priority.ToString(),
            optimisticHours  = t.EstimatedOptimisticHours  ?? 0,
            mostLikelyHours  = t.EstimatedMostLikelyHours  ?? 0,
            pessimisticHours = t.EstimatedPessimisticHours ?? 0
        };

        private static object MapTestCase(TestCase tc) => new
        {
            id = tc.Id, title = tc.Title, steps = tc.Steps, expectedResult = tc.ExpectedResult
        };
    }
}
