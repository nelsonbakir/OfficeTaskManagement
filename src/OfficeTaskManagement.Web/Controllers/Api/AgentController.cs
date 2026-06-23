using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Agent;
using OfficeTaskManagement.Services.Codebase;
using System.Security.Claims;

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

    public AgentController(
        IConfiguration config,
        CodebaseIndexingService indexer,
        IAgentService agent,
        ApplicationDbContext db)
    {
        _config  = config;
        _indexer = indexer;
        _agent   = agent;
        _db      = db;
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
        var tenantId = User.FindFirstValue("TenantId") ?? "";
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

    // POST /api/agent/chat
    // Multi-turn AI copilot chat endpoint.
    [HttpPost("chat")]
    [Authorize]
    public async Task<ActionResult<AgentChatResponse>> ChatAsync(
        [FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var tenantId = User.FindFirstValue("TenantId") ?? "";

        // Enrich request with server-side resolved identity — client cannot spoof these
        var enriched = request with { UserId = userId, TenantId = tenantId };

        var response = await _agent.ChatAsync(enriched, ct);
        return Ok(response);
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
}
