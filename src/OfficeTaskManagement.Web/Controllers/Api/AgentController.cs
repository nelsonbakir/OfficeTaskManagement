using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Agent;
using OfficeTaskManagement.Services.Codebase;
using System.Security.Claims;
using System.Text.Json;

namespace OfficeTaskManagement.Controllers.Api;

/// <summary>
/// Agent controller — hosts the reindex webhook (Phase 3) and
/// the multi-turn copilot chat endpoint (Phase 4).
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → Git Webhook
///       ai-agent-plan/05_SERVICE_LAYER.md → AgentService
/// </summary>
[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly CodebaseIndexingService _indexer;
    private readonly IAgentService _agent;
    private readonly ApplicationDbContext _db;
    private readonly MentionSearchService _mentionSearch;
    private readonly AgentConversationService _conversationService;
    private readonly OfficeTaskManagement.Services.Ai.AiQueuedJobService _queuedJobService;
    private readonly OfficeTaskManagement.Services.Ai.IGeminiAiService _aiService;

    public AgentController(
        IConfiguration config,
        CodebaseIndexingService indexer,
        IAgentService agent,
        ApplicationDbContext db,
        MentionSearchService mentionSearch,
        AgentConversationService conversationService,
        OfficeTaskManagement.Services.Ai.AiQueuedJobService queuedJobService,
        OfficeTaskManagement.Services.Ai.IGeminiAiService aiService)
    {
        _config  = config;
        _indexer = indexer;
        _agent   = agent;
        _db      = db;
        _mentionSearch = mentionSearch;
        _conversationService = conversationService;
        _queuedJobService = queuedJobService;
        _aiService = aiService;
    }

    // POST /api/agent/index-project/{projectId}
    // Triggers codebase indexing for a project (non-blocking).
    [HttpPost("index-project/{projectId}")]
    [Authorize]
    public IActionResult IndexProject(int projectId)
    {
        _ = Task.Run(() => _indexer.IndexProjectAsync(projectId, CancellationToken.None));
        return Accepted(new { message = "Project codebase indexing started." });
    }

    // DELETE /api/agent/index-project/{projectId}
    // Purges project codebase embeddings index.
    [HttpDelete("index-project/{projectId}")]
    [Authorize]
    public async Task<IActionResult> PurgeProjectIndexAsync(int projectId)
    {
        await _indexer.PurgeProjectIndexAsync(projectId);
        return NoContent();
    }

    // GET /api/agent/index-status/{projectId}
    // Returns codebase indexing statistics and costs.
    [HttpGet("index-status/{projectId}")]
    [Authorize]
    public async Task<IActionResult> GetIndexStatusAsync(int projectId, CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound("Project not found.");

        var chunkCount = await _db.CodeEmbeddings.CountAsync(e => e.ProjectId == projectId, ct);

        // Check if there are any newer changes
        bool needsSync = false;
        var lastIndexed = await _db.CodeEmbeddings
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.IndexedAt)
            .Select(e => (DateTimeOffset?)e.IndexedAt)
            .FirstOrDefaultAsync(ct);

        if (lastIndexed.HasValue && !string.IsNullOrEmpty(project.RepositoryPath) && Directory.Exists(project.RepositoryPath))
        {
            var directoryInfo = new DirectoryInfo(project.RepositoryPath);
            needsSync = HasNewerFiles(directoryInfo, lastIndexed.Value);
        }
        else if (chunkCount == 0 && !string.IsNullOrEmpty(project.RepositoryPath) && Directory.Exists(project.RepositoryPath))
        {
            needsSync = true;
        }

        // Calculate token/cost estimation from AiEstimationLogs
        var logs = await _db.AiEstimationLogs
            .Where(l => l.TenantId == tenantId && l.EntityType == "Project" && l.EntityId == projectId)
            .Select(l => new { l.InputTokens, l.OutputTokens })
            .ToListAsync(ct);

        var inputTokens = logs.Sum(l => l.InputTokens);
        var outputTokens = logs.Sum(l => l.OutputTokens);
        // Cost: input ($0.075/1M), output ($0.30/1M)
        var costBdt = (inputTokens * 0.075m + outputTokens * 0.30m) / 1000000m * 115m;

        var progress = _indexer.GetProgress(projectId);

        return Ok(new
        {
            projectId,
            repositoryPath = project.RepositoryPath,
            repositoryUrl = project.RepositoryUrl,
            chunkCount,
            lastIndexedAt = lastIndexed,
            needsSync,
            indexingStatus = progress?.Status ?? (chunkCount > 0 ? "Completed" : "NotStarted"),
            indexingError = progress?.ErrorMessage,
            inputTokens,
            outputTokens,
            estimatedCostBdt = Math.Round(costBdt, 2)
        });
    }

    private bool HasNewerFiles(DirectoryInfo dir, DateTimeOffset since)
    {
        var skipDirs = new[] { "bin", "obj", "node_modules", ".git", ".vs", "wwwroot/lib", "wwwroot\\lib", "Migrations" };
        var skipExtensions = new[] { ".min.js", ".min.css", ".map", ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".pdf" };

        foreach (var file in dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            var normalized = file.FullName.Replace('\\', '/');
            if (skipDirs.Any(d => normalized.Contains("/" + d + "/", StringComparison.OrdinalIgnoreCase)) ||
                skipExtensions.Any(e => file.Extension.Equals(e, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (file.LastWriteTimeUtc > since.UtcDateTime)
            {
                return true;
            }
        }
        return false;
    }

    // GET /api/agent/mention-search
    // Autocomplete searching for mentioned entities.
    [HttpGet("mention-search")]
    [Authorize]
    public async Task<IActionResult> MentionSearchAsync(
        [FromQuery] string q,
        [FromQuery] string? types,
        [FromQuery] int? projectId,
        CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        string[]? typesArray = string.IsNullOrEmpty(types) 
            ? null 
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var results = await _mentionSearch.SearchAsync(q, typesArray, projectId, User, tenantId, ct);
        return Ok(results);
    }

    // POST /api/agent/chat
    // Multi-turn AI copilot chat endpoint.
    [HttpPost("chat")]
    [Authorize]
    public async Task<ActionResult<AgentChatResponse>> ChatAsync(
        [FromBody] AgentChatRequest request, CancellationToken ct)
    {
        try
        {
            var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var tenantId = _db.CurrentTenantId;

            // Enrich request with server-side resolved identity — client cannot spoof these
            var enriched = request with { UserId = userId, TenantId = tenantId };

            var response = await _agent.ChatAsync(enriched, ct);
            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, "Request was canceled.");
        }
    }

    // DELETE /api/agent/conversation/{id}
    // Clears a conversation's history (user-initiated reset).
    [HttpDelete("conversation/{id}")]
    [Authorize]
    public async Task<IActionResult> ClearConversationAsync(string id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        await _agent.ClearConversationAsync(id, userId, ct);
        return NoContent();
    }

    // GET /api/agent/user-projects
    // Lists all active projects for the current tenant.
    [HttpGet("user-projects")]
    [Authorize]
    public async Task<IActionResult> GetUserProjectsAsync(CancellationToken ct)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { id = p.Id, name = p.Name })
            .ToListAsync(ct);
        return Ok(projects);
    }

    // GET /api/agent/project-sessions/{projectId}
    // Lists all active chat sessions for the selected project context.
    [HttpGet("project-sessions/{projectId}")]
    [Authorize]
    public async Task<IActionResult> GetProjectSessionsAsync(int projectId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var sessions = await _conversationService.GetProjectSessionsAsync(projectId, userId, ct);
        return Ok(sessions);
    }

    // GET /api/agent/conversation-history/{conversationId}
    // Retrieves conversation history turns.
    [HttpGet("conversation-history/{conversationId}")]
    [Authorize]
    public async Task<IActionResult> GetConversationHistoryAsync(string conversationId, CancellationToken ct)
    {
        var turns = await _conversationService.GetTurnsAsync(conversationId, ct);
        return Ok(turns);
    }

    // GET /api/agent/failed-jobs
    // Lists all failed/queued jobs for the current user and tenant.
    [HttpGet("failed-jobs")]
    [Authorize]
    public async Task<IActionResult> GetFailedJobsAsync([FromQuery] int? projectId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var tenantId = _db.CurrentTenantId;
        var jobs = await _queuedJobService.GetJobsAsync(tenantId, userId, projectId);
        return Ok(jobs);
    }

    // DELETE /api/agent/failed-jobs/{jobId}
    // Deletes/dismisses a failed job.
    [HttpDelete("failed-jobs/{jobId}")]
    [Authorize]
    public async Task<IActionResult> DeleteFailedJobAsync(string jobId)
    {
        var deleted = await _queuedJobService.DeleteJobAsync(jobId);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // POST /api/agent/failed-jobs/{jobId}/resume
    // Resumes (retries) a failed job.
    [HttpPost("failed-jobs/{jobId}/resume")]
    [Authorize]
    public async Task<IActionResult> ResumeFailedJobAsync(string jobId, [FromQuery] string? conversationId, CancellationToken ct)
    {
        var job = await _queuedJobService.GetJobByIdAsync(jobId);
        if (job == null) return NotFound();

        try
        {
            if (job.JobType == "Chat")
            {
                var request = JsonSerializer.Deserialize<AgentChatRequest>(job.RequestPayloadJson);
                if (request == null) return BadRequest("Invalid job payload.");

                // Re-run the chat
                var response = await _agent.ChatAsync(request, ct);

                // If successful, delete the job from queue
                await _queuedJobService.DeleteJobAsync(jobId);
                return Ok(new { success = true, jobType = "Chat", result = response });
            }
            else if (job.JobType == "Estimation")
            {
                var request = JsonSerializer.Deserialize<EstimationRequest>(job.RequestPayloadJson);
                if (request == null) return BadRequest("Invalid job payload.");

                var result = await _aiService.EstimateAsync(request, ct);

                // Apply to Task if it exists in DB
                if (job.EntityId.HasValue && request.EntityType == "Task")
                {
                    var task = await _db.Tasks.FindAsync(job.EntityId.Value);
                    if (task != null)
                    {
                        task.EstimatedOptimisticHours = result.OptimisticHours;
                        task.EstimatedMostLikelyHours = result.MostLikelyHours;
                        task.EstimatedPessimisticHours = result.PessimisticHours;
                        task.PertEstimatedHours = result.PertHours;
                        task.EstimatedHours = result.PertHours; // baseline
                        await _db.SaveChangesAsync(ct);
                    }
                }

                // Append notification turn to active conversation if provided
                if (!string.IsNullOrEmpty(conversationId))
                {
                    var resultText = $"✅ **AI Estimation Resumed & Completed** for {request.EntityType} *\"{request.Title}\"*:\n" +
                                     $"- **Optimistic Hours:** {result.OptimisticHours}h\n" +
                                     $"- **Most Likely Hours:** {result.MostLikelyHours}h\n" +
                                     $"- **Pessimistic Hours:** {result.PessimisticHours}h\n" +
                                     $"- **PERT Estimate:** {result.PertHours}h\n\n" +
                                     $"*Rationale:* {result.Rationale}";
                    await _conversationService.AppendTurnAsync(conversationId, "model", resultText, ct);
                }

                await _queuedJobService.DeleteJobAsync(jobId);
                return Ok(new { success = true, jobType = "Estimation", result });
            }
            else if (job.JobType == "ReEstimation")
            {
                var request = JsonSerializer.Deserialize<ReEstimationRequest>(job.RequestPayloadJson);
                if (request == null) return BadRequest("Invalid job payload.");

                var result = await _aiService.ReEstimateAsync(request, ct);

                // Apply to Task if it exists in DB
                if (request.EntityId > 0 && request.EntityType == "Task")
                {
                    var task = await _db.Tasks.FindAsync(request.EntityId);
                    if (task != null)
                    {
                        task.EstimatedOptimisticHours = result.OptimisticHours;
                        task.EstimatedMostLikelyHours = result.MostLikelyHours;
                        task.EstimatedPessimisticHours = result.PessimisticHours;
                        task.PertEstimatedHours = result.PertHours;
                        task.EstimatedHours = result.PertHours; // baseline
                        await _db.SaveChangesAsync(ct);
                    }
                }

                // Append notification turn to active conversation if provided
                if (!string.IsNullOrEmpty(conversationId))
                {
                    var resultText = $"✅ **AI Re-Estimation Resumed & Completed** for {request.EntityType} #{request.EntityId} *\"{request.Title}\"*:\n" +
                                     $"- **New PERT Estimate:** {result.PertHours}h\n" +
                                     $"- **Estimated Budget BDT:** {result.EstimatedBudgetBDT} BDT\n\n" +
                                     $"*Rationale:* {result.Rationale}";
                    await _conversationService.AppendTurnAsync(conversationId, "model", resultText, ct);
                }

                await _queuedJobService.DeleteJobAsync(jobId);
                return Ok(new { success = true, jobType = "ReEstimation", result });
            }
            else if (job.JobType == "AcceptanceCriteria")
            {
                using var payloadDoc = JsonDocument.Parse(job.RequestPayloadJson);
                var root = payloadDoc.RootElement;
                var title = root.GetProperty("title").GetString() ?? "";
                var description = root.GetProperty("description").GetString() ?? "";
                var userStoryId = root.TryGetProperty("userStoryId", out var us) ? us.GetInt32() : 0;

                var result = await _aiService.GenerateAcceptanceCriteriaAsync(title, description, ct);

                // Apply to UserStory if it exists in DB
                if (userStoryId > 0)
                {
                    var story = await _db.UserStories.FindAsync(userStoryId);
                    if (story != null)
                    {
                        story.AcceptanceCriteria = result;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                // Append notification turn to active conversation if provided
                if (!string.IsNullOrEmpty(conversationId))
                {
                    var resultText = $"✅ **Acceptance Criteria Resumed & Completed** for User Story #{userStoryId} *\"{title}\"*:\n\n{result}";
                    await _conversationService.AppendTurnAsync(conversationId, "model", resultText, ct);
                }

                await _queuedJobService.DeleteJobAsync(jobId);
                return Ok(new { success = true, jobType = "AcceptanceCriteria", result });
            }

            return BadRequest("Unsupported job type.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Resume failed: {ex.Message}");
        }
    }
}
