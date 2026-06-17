using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.Codebase;
using OfficeTaskManagement.Services.WorkflowEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    [ApiController]
    [Route("api/onboard")]
    [Authorize]
    public class ProjectInitiationApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeminiAiService _ai;
        private readonly GitCloneService _cloneService;
        private readonly CodebaseIndexingService _indexer;
        private readonly IWorkflowEngineService _workflowEngine;
        private readonly ILogger<ProjectInitiationApiController> _logger;

        public ProjectInitiationApiController(
            ApplicationDbContext db,
            IGeminiAiService ai,
            GitCloneService cloneService,
            CodebaseIndexingService indexer,
            IWorkflowEngineService workflowEngine,
            ILogger<ProjectInitiationApiController> logger)
        {
            _db = db;
            _ai = ai;
            _cloneService = cloneService;
            _indexer = indexer;
            _workflowEngine = workflowEngine;
            _logger = logger;
        }

        // POST /api/onboard/clone/{projectId}
        [HttpPost("clone/{projectId}")]
        public async Task<IActionResult> CloneProjectRepositoryAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects.FindAsync(new object[] { projectId }, ct);
            if (project == null) return NotFound("Project not found.");

            if (string.IsNullOrWhiteSpace(project.RepositoryUrl))
            {
                return BadRequest("Project does not have a Git Repository URL configured.");
            }

            // Clone repository outside of wwwroot for security
            var targetDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "CodebaseClones", $"project-{projectId}");
            
            try
            {
                var localPath = await _cloneService.CloneRepositoryAsync(project.RepositoryUrl, targetDir, ct);
                
                // Update RepositoryPath in DB
                project.RepositoryPath = localPath;
                await _db.SaveChangesAsync(ct);

                return Ok(new { message = "Repository cloned successfully.", repositoryPath = localPath });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clone repository for project {ProjectId}", projectId);
                return StatusCode(500, $"Failed to clone repository: {ex.Message}");
            }
        }

        // GET /api/onboard/state/{projectId}
        [HttpGet("state/{projectId}")]
        public async Task<IActionResult> GetOnboardingStateAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);

            if (project == null) return NotFound("Project not found.");

            // Set tenant context for queries
            var tenantProvider = HttpContext.RequestServices.GetService<ITenantProvider>();
            tenantProvider?.SetTenant(project.TenantId);

            // Reconstruct the tree from DB using the project tenant
            var epics = await _db.Epics
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.Id)
                .ToListAsync(ct);

            var epicDtos = new List<object>();

            foreach (var epic in epics)
            {
                var features = await _db.Features
                    .Where(f => f.EpicId == epic.Id)
                    .OrderBy(f => f.Id)
                    .ToListAsync(ct);

                var featureDtos = new List<object>();

                foreach (var feature in features)
                {
                    var stories = await _db.UserStories
                        .Where(s => s.FeatureId == feature.Id)
                        .OrderBy(s => s.Id)
                        .ToListAsync(ct);

                    var storyDtos = new List<object>();

                    foreach (var story in stories)
                    {
                        var tasks = await _db.Tasks
                            .Where(t => t.UserStoryId == story.Id)
                            .OrderBy(t => t.Id)
                            .ToListAsync(ct);

                        var testCases = await _db.TestCases
                            .Where(tc => tc.UserStoryId == story.Id)
                            .OrderBy(tc => tc.Id)
                            .ToListAsync(ct);

                        storyDtos.Add(new
                        {
                            id = story.Id,
                            title = story.Title,
                            description = story.Description,
                            acceptanceCriteria = story.AcceptanceCriteria,
                            priority = story.Priority.ToString(),
                            selected = true,
                            tasks = tasks.Select(t => new
                            {
                                id = t.Id,
                                title = t.Title,
                                description = t.Description,
                                priority = t.Priority.ToString(),
                                optimisticHours = t.EstimatedOptimisticHours ?? 0,
                                mostLikelyHours = t.EstimatedMostLikelyHours ?? 0,
                                pessimisticHours = t.EstimatedPessimisticHours ?? 0
                            }).ToList(),
                            testCases = testCases.Select(tc => new
                            {
                                id = tc.Id,
                                title = tc.Title,
                                steps = tc.Steps,
                                expectedResult = tc.ExpectedResult
                            }).ToList()
                        });
                    }

                    featureDtos.Add(new
                    {
                        id = feature.Id,
                        name = feature.Name,
                        description = feature.Description,
                        selected = true,
                        userStories = storyDtos
                    });
                }

                epicDtos.Add(new
                {
                    id = epic.Id,
                    name = epic.Name,
                    description = epic.Description,
                    selected = true,
                    features = featureDtos
                });
            }

            return Ok(new
            {
                projectId = project.Id,
                techStack = "N/A",
                projectSummary = project.Description ?? "",
                testOverview = "N/A",
                testsAbsentOrIncomplete = true,
                epics = epicDtos
            });
        }

        // POST /api/onboard/analyze-project/{projectId}
        [HttpPost("analyze-project/{projectId}")]
        public async Task<ActionResult<ProjectAnalysisResult>> AnalyzeProjectAsync(int projectId, CancellationToken ct)
        {
            try
            {
                var result = await _ai.AnalyzeProjectCodebaseAsync(projectId, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Project codebase analysis failed for project {ProjectId}", projectId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/save-epics
        [HttpPost("save-epics")]
        public async Task<IActionResult> SaveEpicsAsync([FromBody] SaveEpicsRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("Invalid request.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existingEpics = await _db.Epics
                    .IgnoreQueryFilters()
                    .Where(e => e.ProjectId == project.Id)
                    .ToListAsync(ct);

                var requestedEpicIds = request.Epics.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).ToList();

                var toDelete = existingEpics.Where(e => !requestedEpicIds.Contains(e.Id)).ToList();
                _db.Epics.RemoveRange(toDelete);

                var savedEpics = new List<object>();

                foreach (var reqEpic in request.Epics)
                {
                    Epic epic;
                    if (reqEpic.Id.HasValue)
                    {
                        epic = existingEpics.First(e => e.Id == reqEpic.Id.Value);
                        epic.Name = reqEpic.Name;
                        epic.Description = reqEpic.Description;
                        _db.Epics.Update(epic);
                    }
                    else
                    {
                        epic = new Epic
                        {
                            ProjectId = project.Id,
                            Name = reqEpic.Name,
                            Description = reqEpic.Description,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.Epics.Add(epic);
                    }

                    await _db.SaveChangesAsync(ct);
                    savedEpics.Add(new { id = epic.Id, name = epic.Name, description = epic.Description, selected = true, features = new List<object>() });
                }

                await tx.CommitAsync(ct);
                return Ok(savedEpics);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to save epics for project {ProjectId}", request.ProjectId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/analyze-features/{epicId}
        [HttpPost("analyze-features/{epicId}")]
        public async Task<IActionResult> AnalyzeFeaturesForEpicAsync(int epicId, CancellationToken ct)
        {
            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == epicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            try
            {
                var featuresDto = await _ai.SuggestFeaturesForEpicAsync(project.Id, epic.Name, epic.Description ?? "", ct);

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var existing = await _db.Features.IgnoreQueryFilters().Where(f => f.EpicId == epic.Id).ToListAsync(ct);
                    _db.Features.RemoveRange(existing);
                    await _db.SaveChangesAsync(ct);

                    var result = new List<object>();
                    foreach (var f in featuresDto)
                    {
                        var feat = new Feature
                        {
                            EpicId = epic.Id,
                            Name = f.Name,
                            Description = f.Description,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.Features.Add(feat);
                        await _db.SaveChangesAsync(ct);
                        result.Add(new { id = feat.Id, name = feat.Name, description = feat.Description, selected = true, userStories = new List<object>() });
                    }

                    await tx.CommitAsync(ct);
                    return Ok(result);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze features for Epic {EpicId}", epicId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/save-features
        [HttpPost("save-features")]
        public async Task<IActionResult> SaveFeaturesAsync([FromBody] SaveFeaturesRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("Invalid request.");

            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == request.EpicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await _db.Features.IgnoreQueryFilters().Where(f => f.EpicId == epic.Id).ToListAsync(ct);
                var requestedIds = request.Features.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToList();

                var toDelete = existing.Where(f => !requestedIds.Contains(f.Id)).ToList();
                _db.Features.RemoveRange(toDelete);

                var savedFeatures = new List<object>();

                foreach (var reqFeat in request.Features)
                {
                    Feature feat;
                    if (reqFeat.Id.HasValue)
                    {
                        feat = existing.First(f => f.Id == reqFeat.Id.Value);
                        feat.Name = reqFeat.Name;
                        feat.Description = reqFeat.Description;
                        _db.Features.Update(feat);
                    }
                    else
                    {
                        feat = new Feature
                        {
                            EpicId = epic.Id,
                            Name = reqFeat.Name,
                            Description = reqFeat.Description,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.Features.Add(feat);
                    }

                    await _db.SaveChangesAsync(ct);
                    savedFeatures.Add(new { id = feat.Id, name = feat.Name, description = feat.Description, selected = true, userStories = new List<object>() });
                }

                await tx.CommitAsync(ct);
                return Ok(savedFeatures);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to save features for Epic {EpicId}", request.EpicId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/analyze-stories/{featureId}
        [HttpPost("analyze-stories/{featureId}")]
        public async Task<IActionResult> AnalyzeStoriesForFeatureAsync(int featureId, CancellationToken ct)
        {
            var feat = await _db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == featureId, ct);
            if (feat == null) return NotFound("Feature not found.");

            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == feat.EpicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            try
            {
                var storiesDto = await _ai.SuggestUserStoriesForFeatureAsync(project.Id, epic.Name, feat.Name, feat.Description ?? "", ct);

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var existing = await _db.UserStories.IgnoreQueryFilters().Where(s => s.FeatureId == feat.Id).ToListAsync(ct);
                    _db.UserStories.RemoveRange(existing);
                    await _db.SaveChangesAsync(ct);

                    var result = new List<object>();
                    foreach (var s in storiesDto)
                    {
                        var story = new UserStory
                        {
                            FeatureId = feat.Id,
                            Title = s.Title,
                            Description = s.Description,
                            AcceptanceCriteria = s.AcceptanceCriteria,
                            Priority = Enum.TryParse<TaskPriority>(s.Priority, out var sp) ? sp : TaskPriority.Medium,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.UserStories.Add(story);
                        await _db.SaveChangesAsync(ct);
                        result.Add(new
                        {
                            id = story.Id,
                            title = story.Title,
                            description = story.Description,
                            acceptanceCriteria = story.AcceptanceCriteria,
                            priority = story.Priority.ToString(),
                            selected = true,
                            tasks = new List<object>(),
                            testCases = new List<object>()
                        });
                    }

                    await tx.CommitAsync(ct);
                    return Ok(result);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze stories for Feature {FeatureId}", featureId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/save-stories
        [HttpPost("save-stories")]
        public async Task<IActionResult> SaveStoriesAsync([FromBody] SaveStoriesRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("Invalid request.");

            var feat = await _db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == request.FeatureId, ct);
            if (feat == null) return NotFound("Feature not found.");

            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == feat.EpicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await _db.UserStories.IgnoreQueryFilters().Where(s => s.FeatureId == feat.Id).ToListAsync(ct);
                var requestedIds = request.Stories.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToList();

                var toDelete = existing.Where(s => !requestedIds.Contains(s.Id)).ToList();
                _db.UserStories.RemoveRange(toDelete);

                var savedStories = new List<object>();

                foreach (var reqStory in request.Stories)
                {
                    UserStory story;
                    if (reqStory.Id.HasValue)
                    {
                        story = existing.First(s => s.Id == reqStory.Id.Value);
                        story.Title = reqStory.Title;
                        story.Description = reqStory.Description;
                        story.AcceptanceCriteria = reqStory.AcceptanceCriteria;
                        story.Priority = Enum.TryParse<TaskPriority>(reqStory.Priority, out var sp) ? sp : TaskPriority.Medium;
                        _db.UserStories.Update(story);
                    }
                    else
                    {
                        story = new UserStory
                        {
                            FeatureId = feat.Id,
                            Title = reqStory.Title,
                            Description = reqStory.Description,
                            AcceptanceCriteria = reqStory.AcceptanceCriteria,
                            Priority = Enum.TryParse<TaskPriority>(reqStory.Priority, out var sp) ? sp : TaskPriority.Medium,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.UserStories.Add(story);
                    }

                    await _db.SaveChangesAsync(ct);
                    savedStories.Add(new
                    {
                        id = story.Id,
                        title = story.Title,
                        description = story.Description,
                        acceptanceCriteria = story.AcceptanceCriteria,
                        priority = story.Priority.ToString(),
                        selected = true,
                        tasks = new List<object>(),
                        testCases = new List<object>()
                    });
                }

                await tx.CommitAsync(ct);
                return Ok(savedStories);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to save stories for Feature {FeatureId}", request.FeatureId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/analyze-tasks-tests/{storyId}
        [HttpPost("analyze-tasks-tests/{storyId}")]
        public async Task<IActionResult> AnalyzeTasksAndTestsForStoryAsync(int storyId, CancellationToken ct)
        {
            var story = await _db.UserStories.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story == null) return NotFound("User Story not found.");

            var feat = await _db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == story.FeatureId, ct);
            if (feat == null) return NotFound("Feature not found.");

            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == feat.EpicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            try
            {
                var resultDto = await _ai.SuggestTasksAndTestCasesAsync(project.Id, story.Title, story.Description ?? "", true, ct);

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var existingTasks = await _db.Tasks.IgnoreQueryFilters().Where(t => t.UserStoryId == story.Id).ToListAsync(ct);
                    _db.Tasks.RemoveRange(existingTasks);

                    var existingTests = await _db.TestCases.IgnoreQueryFilters().Where(tc => tc.UserStoryId == story.Id).ToListAsync(ct);
                    _db.TestCases.RemoveRange(existingTests);

                    await _db.SaveChangesAsync(ct);

                    var taskResult = new List<object>();
                    var testResult = new List<object>();

                    foreach (var t in resultDto.Tasks)
                    {
                        decimal o = t.OptimisticHours;
                        decimal m = t.MostLikelyHours;
                        decimal p = t.PessimisticHours;
                        decimal pert = _workflowEngine.CalculatePert(o, m, p);

                        var task = new TaskItem
                        {
                            UserStoryId = story.Id,
                            ProjectId = project.Id,
                            EpicId = epic.Id,
                            FeatureId = feat.Id,
                            Title = t.Title,
                            Description = t.Description,
                            Priority = Enum.TryParse<TaskPriority>(t.Priority, out var tp) ? tp : TaskPriority.Medium,
                            EstimatedOptimisticHours = o > 0 ? o : null,
                            EstimatedMostLikelyHours = m > 0 ? m : null,
                            EstimatedPessimisticHours = p > 0 ? p : null,
                            PertEstimatedHours = pert > 0 ? pert : null,
                            EstimatedHours = pert > 0 ? pert : m,
                            Status = Models.Enums.TaskStatus.New,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.Tasks.Add(task);
                        await _db.SaveChangesAsync(ct);
                        taskResult.Add(new
                        {
                            id = task.Id,
                            title = task.Title,
                            description = task.Description,
                            priority = task.Priority.ToString(),
                            optimisticHours = o,
                            mostLikelyHours = m,
                            pessimisticHours = p
                        });
                    }

                    foreach (var tcDto in resultDto.TestCases)
                    {
                        var tc = new TestCase
                        {
                            UserStoryId = story.Id,
                            Title = tcDto.Title,
                            Steps = tcDto.Steps,
                            ExpectedResult = tcDto.ExpectedResult,
                            IsAutomated = false,
                            IsPassed = false,
                            TenantId = project.TenantId
                        };
                        _db.TestCases.Add(tc);
                        await _db.SaveChangesAsync(ct);
                        testResult.Add(new
                        {
                            id = tc.Id,
                            title = tc.Title,
                            steps = tc.Steps,
                            expectedResult = tc.ExpectedResult
                        });
                    }

                    await tx.CommitAsync(ct);
                    return Ok(new { tasks = taskResult, testCases = testResult });
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze tasks/tests for Story {StoryId}", storyId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/save-tasks-tests
        [HttpPost("save-tasks-tests")]
        public async Task<IActionResult> SaveTasksAndTestsAsync([FromBody] SaveTasksAndTestsRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("Invalid request.");

            var story = await _db.UserStories.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == request.StoryId, ct);
            if (story == null) return NotFound("User Story not found.");

            var feat = await _db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == story.FeatureId, ct);
            if (feat == null) return NotFound("Feature not found.");

            var epic = await _db.Epics.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == feat.EpicId, ct);
            if (epic == null) return NotFound("Epic not found.");

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == epic.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Manage Tasks
                var existingTasks = await _db.Tasks.IgnoreQueryFilters().Where(t => t.UserStoryId == story.Id).ToListAsync(ct);
                var requestedTaskIds = request.Tasks.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToList();

                var tasksToDelete = existingTasks.Where(t => !requestedTaskIds.Contains(t.Id)).ToList();
                _db.Tasks.RemoveRange(tasksToDelete);

                var taskResult = new List<object>();
                foreach (var reqTask in request.Tasks)
                {
                    decimal o = reqTask.OptimisticHours;
                    decimal m = reqTask.MostLikelyHours;
                    decimal p = reqTask.PessimisticHours;
                    decimal pert = _workflowEngine.CalculatePert(o, m, p);

                    TaskItem task;
                    if (reqTask.Id.HasValue)
                    {
                        task = existingTasks.First(t => t.Id == reqTask.Id.Value);
                        task.Title = reqTask.Title;
                        task.Description = reqTask.Description;
                        task.Priority = Enum.TryParse<TaskPriority>(reqTask.Priority, out var tp) ? tp : TaskPriority.Medium;
                        task.EstimatedOptimisticHours = o > 0 ? o : null;
                        task.EstimatedMostLikelyHours = m > 0 ? m : null;
                        task.EstimatedPessimisticHours = p > 0 ? p : null;
                        task.PertEstimatedHours = pert > 0 ? pert : null;
                        task.EstimatedHours = pert > 0 ? pert : m;
                        _db.Tasks.Update(task);
                    }
                    else
                    {
                        task = new TaskItem
                        {
                            UserStoryId = story.Id,
                            ProjectId = project.Id,
                            EpicId = epic.Id,
                            FeatureId = feat.Id,
                            Title = reqTask.Title,
                            Description = reqTask.Description,
                            Priority = Enum.TryParse<TaskPriority>(reqTask.Priority, out var tp) ? tp : TaskPriority.Medium,
                            EstimatedOptimisticHours = o > 0 ? o : null,
                            EstimatedMostLikelyHours = m > 0 ? m : null,
                            EstimatedPessimisticHours = p > 0 ? p : null,
                            PertEstimatedHours = pert > 0 ? pert : null,
                            EstimatedHours = pert > 0 ? pert : m,
                            Status = Models.Enums.TaskStatus.New,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = project.TenantId
                        };
                        _db.Tasks.Add(task);
                    }

                    await _db.SaveChangesAsync(ct);
                    taskResult.Add(new
                    {
                        id = task.Id,
                        title = task.Title,
                        description = task.Description,
                        priority = task.Priority.ToString(),
                        optimisticHours = o,
                        mostLikelyHours = m,
                        pessimisticHours = p
                    });
                }

                // Manage Test Cases
                var existingTests = await _db.TestCases.IgnoreQueryFilters().Where(tc => tc.UserStoryId == story.Id).ToListAsync(ct);
                var requestedTestIds = request.TestCases.Where(tc => tc.Id.HasValue).Select(tc => tc.Id!.Value).ToList();

                var testsToDelete = existingTests.Where(tc => !requestedTestIds.Contains(tc.Id)).ToList();
                _db.TestCases.RemoveRange(testsToDelete);

                var testResult = new List<object>();
                foreach (var reqTest in request.TestCases)
                {
                    TestCase tc;
                    if (reqTest.Id.HasValue)
                    {
                        tc = existingTests.First(tc => tc.Id == reqTest.Id.Value);
                        tc.Title = reqTest.Title;
                        tc.Steps = reqTest.Steps;
                        tc.ExpectedResult = reqTest.ExpectedResult;
                        _db.TestCases.Update(tc);
                    }
                    else
                    {
                        tc = new TestCase
                        {
                            UserStoryId = story.Id,
                            Title = reqTest.Title,
                            Steps = reqTest.Steps,
                            ExpectedResult = reqTest.ExpectedResult,
                            IsAutomated = false,
                            IsPassed = false,
                            TenantId = project.TenantId
                        };
                        _db.TestCases.Add(tc);
                    }

                    await _db.SaveChangesAsync(ct);
                    testResult.Add(new
                    {
                        id = tc.Id,
                        title = tc.Title,
                        steps = tc.Steps,
                        expectedResult = tc.ExpectedResult
                    });
                }

                await tx.CommitAsync(ct);
                return Ok(new { tasks = taskResult, testCases = testResult });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to save tasks/tests for Story {StoryId}", request.StoryId);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/complete/{projectId}
        [HttpPost("complete/{projectId}")]
        public async Task<IActionResult> CompleteOnboardingAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var now = DateTime.UtcNow;

            project.StrategicStatus = ProjectStrategicStatus.Active;
            project.StrategicStatusChangedAt = now;
            project.StrategicStatusChangedById = userId;
            project.StrategicStatusReason = "Initiated step-by-step via codebase-first onboarding wizard.";
            
            _db.Projects.Update(project);
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = "Project successfully activated." });
        }
    }
}
