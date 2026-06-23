using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Codebase;
using OfficeTaskManagement.Services.Onboarding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    /// <summary>
    /// Thin REST façade for the codebase-first onboarding wizard.
    /// All heavy logic lives in <see cref="OnboardingOrchestrationService"/>.
    /// </summary>
    [ApiController]
    [Route("api/onboard")]
    [Authorize]
    public class ProjectInitiationApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IOnboardingOrchestrationService _orchestrator;
        private readonly OnboardingSessionService _session;
        private readonly GitCloneService _cloneService;
        private readonly CodebaseIndexingService _indexer;
        private readonly ILogger<ProjectInitiationApiController> _logger;

        public ProjectInitiationApiController(
            ApplicationDbContext db,
            IOnboardingOrchestrationService orchestrator,
            OnboardingSessionService session,
            GitCloneService cloneService,
            CodebaseIndexingService indexer,
            ILogger<ProjectInitiationApiController> logger)
        {
            _db           = db;
            _orchestrator = orchestrator;
            _session      = session;
            _cloneService = cloneService;
            _indexer      = indexer;
            _logger       = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // ── GET /api/onboard/state/{projectId} ────────────────────────────────
        // Returns the existing DB tree + last completed step checkpoint.

        [HttpGet("state/{projectId}")]
        public async Task<IActionResult> GetOnboardingStateAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project == null) return NotFound("Project not found.");

            // Set tenant so multi-tenant filters work
            var tenantProvider = HttpContext.RequestServices.GetService<ITenantProvider>();
            tenantProvider?.SetTenant(project.TenantId);

            // Checkpoint (resume step)
            var checkpoint = await _session.GetOrCreateAsync(projectId, project.TenantId, ct);

            // Rebuild the full tree from DB
            var epics = await _db.Epics
                .Where(e => e.ProjectId == projectId).OrderBy(e => e.Id).ToListAsync(ct);

            var epicDtos = new List<object>();
            foreach (var epic in epics)
            {
                var features = await _db.Features
                    .Where(f => f.EpicId == epic.Id).OrderBy(f => f.Id).ToListAsync(ct);

                var featDtos = new List<object>();
                foreach (var feat in features)
                {
                    var stories = await _db.UserStories
                        .Where(s => s.FeatureId == feat.Id).OrderBy(s => s.Id).ToListAsync(ct);

                    var storyDtos = new List<object>();
                    foreach (var story in stories)
                    {
                        var tasks = await _db.Tasks
                            .Where(t => t.UserStoryId == story.Id).OrderBy(t => t.Id).ToListAsync(ct);
                        var tests = await _db.TestCases
                            .Where(tc => tc.UserStoryId == story.Id).OrderBy(tc => tc.Id).ToListAsync(ct);

                        storyDtos.Add(new {
                            id = story.Id, title = story.Title, description = story.Description,
                            acceptanceCriteria = story.AcceptanceCriteria,
                            priority = story.Priority.ToString(), selected = true,
                            tasks = tasks.Select(t => new {
                                id = t.Id, title = t.Title, description = t.Description,
                                priority = t.Priority.ToString(),
                                optimisticHours  = t.EstimatedOptimisticHours  ?? 0m,
                                mostLikelyHours  = t.EstimatedMostLikelyHours  ?? 0m,
                                pessimisticHours = t.EstimatedPessimisticHours ?? 0m
                            }),
                            testCases = tests.Select(tc => new {
                                id = tc.Id, title = tc.Title,
                                steps = tc.Steps, expectedResult = tc.ExpectedResult
                            })
                        });
                    }

                    featDtos.Add(new {
                        id = feat.Id, name = feat.Name, description = feat.Description,
                        selected = true, userStories = storyDtos
                    });
                }

                epicDtos.Add(new {
                    id = epic.Id, name = epic.Name, description = epic.Description,
                    selected = true, features = featDtos
                });
            }

            return Ok(new {
                projectId           = project.Id,
                lastCompletedStep   = checkpoint.LastCompletedStep,
                isCompleted         = checkpoint.IsCompleted,
                projectSummary      = project.Description ?? "",
                techStack           = "N/A",
                testOverview        = "N/A",
                testsAbsentOrIncomplete = true,
                epics               = epicDtos
            });
        }

        // ── PATCH /api/onboard/checkpoint/{projectId}/{step} ──────────────────
        // Frontend calls this after successfully confirming each step.

        [HttpPatch("checkpoint/{projectId}/{step}")]
        public async Task<IActionResult> SaveCheckpointAsync(int projectId, int step, CancellationToken ct)
        {
            await _session.MarkStepCompleteAsync(projectId, step, ct);
            return Ok(new { step, saved = true });
        }

        // ── POST /api/onboard/clone/{projectId} ───────────────────────────────

        [HttpPost("clone/{projectId}")]
        public async Task<IActionResult> CloneProjectRepositoryAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects.FindAsync(new object[] { projectId }, ct);
            if (project == null) return NotFound("Project not found.");

            if (string.IsNullOrWhiteSpace(project.RepositoryUrl))
                return BadRequest("Project does not have a Git Repository URL configured.");

            var targetDir = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "App_Data", "CodebaseClones", $"project-{projectId}");

            try
            {
                var localPath = await _cloneService.CloneRepositoryAsync(project.RepositoryUrl, targetDir, ct);
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

        // ── POST /api/onboard/analyze-project/{projectId} ────────────────────

        [HttpPost("analyze-project/{projectId}")]
        public async Task<IActionResult> AnalyzeProjectAsync(int projectId, CancellationToken ct)
        {
            try
            {
                var result = await _orchestrator.AnalyzeProjectAsync(projectId, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Project analysis failed for {ProjectId}", projectId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/save-epics ──────────────────────────────────────

        [HttpPost("save-epics")]
        public async Task<IActionResult> SaveEpicsAsync([FromBody] SaveEpicsRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest();
            try
            {
                var result = await _orchestrator.SaveEpicsAsync(request, UserId, ct);
                return Ok(result.Epics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save epics failed for project {ProjectId}", request.ProjectId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/analyze-features/{epicId} ───────────────────────

        [HttpPost("analyze-features/{epicId}")]
        public async Task<IActionResult> AnalyzeFeaturesAsync(int epicId, CancellationToken ct)
        {
            try
            {
                var result = await _orchestrator.AnalyzeFeaturesForEpicAsync(epicId, ct);
                return Ok(new { epicId = result.EpicId, epicName = result.EpicName, features = result.Features });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature analysis failed for epic {EpicId}", epicId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/save-features ───────────────────────────────────

        [HttpPost("save-features")]
        public async Task<IActionResult> SaveFeaturesAsync([FromBody] SaveFeaturesRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest();
            try
            {
                var result = await _orchestrator.SaveFeaturesAsync(request, UserId, ct);
                return Ok(result.Features);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save features failed for epic {EpicId}", request.EpicId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/analyze-stories/{featureId} ────────────────────

        [HttpPost("analyze-stories/{featureId}")]
        public async Task<IActionResult> AnalyzeStoriesAsync(int featureId, CancellationToken ct)
        {
            try
            {
                var result = await _orchestrator.AnalyzeStoriesForFeatureAsync(featureId, ct);
                return Ok(new { featureId = result.FeatureId, featureName = result.FeatureName, stories = result.Stories });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Story analysis failed for feature {FeatureId}", featureId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/save-stories ────────────────────────────────────

        [HttpPost("save-stories")]
        public async Task<IActionResult> SaveStoriesAsync([FromBody] SaveStoriesRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest();
            try
            {
                var result = await _orchestrator.SaveStoriesAsync(request, UserId, ct);
                return Ok(result.Stories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save stories failed for feature {FeatureId}", request.FeatureId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/analyze-tasks-tests/{storyId} ──────────────────

        [HttpPost("analyze-tasks-tests/{storyId}")]
        public async Task<IActionResult> AnalyzeTasksAndTestsAsync(int storyId, CancellationToken ct)
        {
            try
            {
                var result = await _orchestrator.AnalyzeTasksAndTestsForStoryAsync(storyId, ct);
                return Ok(new { storyId = result.StoryId, storyTitle = result.StoryTitle,
                                tasks = result.Tasks, testCases = result.TestCases });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task/test analysis failed for story {StoryId}", storyId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/save-tasks-tests ───────────────────────────────

        [HttpPost("save-tasks-tests")]
        public async Task<IActionResult> SaveTasksAndTestsAsync([FromBody] SaveTasksAndTestsRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest();
            try
            {
                var result = await _orchestrator.SaveTasksAndTestsAsync(request, UserId, ct);
                return Ok(new { tasks = result.Tasks, testCases = result.TestCases });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save tasks/tests failed for story {StoryId}", request.StoryId);
                return StatusCode(500, ex.Message);
            }
        }

        // ── POST /api/onboard/complete/{projectId} ────────────────────────────

        [HttpPost("complete/{projectId}")]
        public async Task<IActionResult> CompleteOnboardingAsync(int projectId, CancellationToken ct)
        {
            try
            {
                await _orchestrator.CompleteOnboardingAsync(projectId, UserId, ct);
                await _session.MarkCompletedAsync(projectId, ct);
                return Ok(new { message = "Project successfully activated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Complete onboarding failed for project {ProjectId}", projectId);
                return StatusCode(500, ex.Message);
            }
        }
    }
}
