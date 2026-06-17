using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
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

        // POST /api/onboard/suggest-features
        [HttpPost("suggest-features")]
        public async Task<ActionResult<List<FeatureSuggestionDto>>> SuggestFeaturesAsync(
            [FromBody] SuggestFeaturesRequest request, CancellationToken ct)
        {
            try
            {
                var result = await _ai.SuggestFeaturesForEpicAsync(request.ProjectId, request.EpicName, request.EpicDescription, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestFeatures failed for Epic {EpicName}", request.EpicName);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/suggest-stories
        [HttpPost("suggest-stories")]
        public async Task<ActionResult<List<UserStorySuggestionDto>>> SuggestStoriesAsync(
            [FromBody] SuggestStoriesRequest request, CancellationToken ct)
        {
            try
            {
                var result = await _ai.SuggestUserStoriesForFeatureAsync(request.ProjectId, request.EpicName, request.FeatureName, request.FeatureDescription, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestStories failed for Feature {FeatureName}", request.FeatureName);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/suggest-tasks-and-tests
        [HttpPost("suggest-tasks-and-tests")]
        public async Task<ActionResult<TaskAndTestCaseSuggestionsDto>> SuggestTasksAndTestsAsync(
            [FromBody] SuggestTasksRequest request, CancellationToken ct)
        {
            try
            {
                var result = await _ai.SuggestTasksAndTestCasesAsync(request.ProjectId, request.StoryTitle, request.StoryDescription, request.SuggestTests, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestTasksAndTests failed for story {StoryTitle}", request.StoryTitle);
                return StatusCode(500, ex.Message);
            }
        }

        // POST /api/onboard/submit-onboarding
        [HttpPost("submit-onboarding")]
        public async Task<IActionResult> SubmitOnboardingAsync([FromBody] OnboardProjectRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("Invalid request.");

            var project = await _db.Projects.FindAsync(new object[] { request.ProjectId }, ct);
            if (project == null) return NotFound("Project not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var tenantId = User.FindFirstValue("TenantId") ?? "default-tenant-id";
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var epicDto in request.Epics)
                {
                    var epic = new Epic
                    {
                        ProjectId = project.Id,
                        Name = epicDto.Name,
                        Description = epicDto.Description,
                        CreatedById = userId,
                        CreatedAt = now,
                        TenantId = tenantId
                    };
                    _db.Epics.Add(epic);
                    await _db.SaveChangesAsync(ct); // Save to generate Epic.Id

                    foreach (var featureDto in epicDto.Features)
                    {
                        var feature = new Feature
                        {
                            EpicId = epic.Id,
                            Name = featureDto.Name,
                            Description = featureDto.Description,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = tenantId
                        };
                        _db.Features.Add(feature);
                        await _db.SaveChangesAsync(ct); // Save to generate Feature.Id

                        foreach (var storyDto in featureDto.UserStories)
                        {
                            var story = new UserStory
                            {
                                FeatureId = feature.Id,
                                Title = storyDto.Title,
                                Description = storyDto.Description,
                                AcceptanceCriteria = storyDto.AcceptanceCriteria,
                                Priority = Enum.TryParse<TaskPriority>(storyDto.Priority, out var sp) ? sp : TaskPriority.Medium,
                                CreatedById = userId,
                                CreatedAt = now,
                                TenantId = tenantId
                            };
                            _db.UserStories.Add(story);
                            await _db.SaveChangesAsync(ct); // Save to generate UserStory.Id

                            foreach (var taskDto in storyDto.Tasks)
                            {
                                decimal o = taskDto.OptimisticHours;
                                decimal m = taskDto.MostLikelyHours;
                                decimal p = taskDto.PessimisticHours;
                                decimal pert = _workflowEngine.CalculatePert(o, m, p);

                                var task = new TaskItem
                                {
                                    UserStoryId = story.Id,
                                    ProjectId = project.Id,
                                    EpicId = epic.Id,
                                    FeatureId = feature.Id,
                                    Title = taskDto.Title,
                                    Description = taskDto.Description,
                                    Priority = Enum.TryParse<TaskPriority>(taskDto.Priority, out var tp) ? tp : TaskPriority.Medium,
                                    EstimatedOptimisticHours = o > 0 ? o : null,
                                    EstimatedMostLikelyHours = m > 0 ? m : null,
                                    EstimatedPessimisticHours = p > 0 ? p : null,
                                    PertEstimatedHours = pert > 0 ? pert : null,
                                    EstimatedHours = pert > 0 ? pert : m,
                                    Status = Models.Enums.TaskStatus.New,
                                    CreatedById = userId,
                                    CreatedAt = now,
                                    TenantId = tenantId
                                };
                                _db.Tasks.Add(task);
                            }

                            foreach (var tcDto in storyDto.TestCases)
                            {
                                var tc = new TestCase
                                {
                                    UserStoryId = story.Id,
                                    Title = tcDto.Title,
                                    Steps = tcDto.Steps,
                                    ExpectedResult = tcDto.ExpectedResult,
                                    IsAutomated = false,
                                    IsPassed = false,
                                    TenantId = tenantId
                                };
                                _db.TestCases.Add(tc);
                            }
                        }
                    }
                }

                // Update Project Strategic Status to Active
                project.StrategicStatus = ProjectStrategicStatus.Active;
                project.StrategicStatusChangedAt = now;
                project.StrategicStatusChangedById = userId;
                project.StrategicStatusReason = "Initiated via codebase-first onboarding wizard.";
                _db.Projects.Update(project);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Ok(new { message = "Project successfully onboarded and initiated." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Transaction failed while submitting onboarding for project {ProjectId}", request.ProjectId);
                return StatusCode(500, $"Transaction failed: {ex.Message}");
            }
        }
    }

    // Request Models
    public record SuggestFeaturesRequest(int ProjectId, string EpicName, string EpicDescription);
    public record SuggestStoriesRequest(int ProjectId, string EpicName, string FeatureName, string FeatureDescription);
    public record SuggestTasksRequest(int ProjectId, string StoryTitle, string StoryDescription, bool SuggestTests);
}
