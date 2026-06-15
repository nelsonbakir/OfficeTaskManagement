using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Codebase;

namespace OfficeTaskManagement.Controllers.Api;

/// <summary>
/// Agent controller — hosts the reindex webhook endpoint (Phase 3)
/// and will host multi-turn copilot chat endpoints (Phase 4).
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → Git Webhook
///       ai-agent-plan/06_API_LAYER.md → AgentController
/// </summary>
[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly CodebaseIndexingService _indexer;

    public AgentController(IConfiguration config, CodebaseIndexingService indexer)
    {
        _config  = config;
        _indexer = indexer;
    }

    // POST /api/agent/reindex
    // Secured with X-Webhook-Secret header. Used by CI/CD (GitHub Actions) after each push.
    [HttpPost("reindex")]
    [AllowAnonymous]
    public IActionResult Reindex(
        [FromHeader(Name = "X-Webhook-Secret")] string? secret)
    {
        var expected = _config["Codebase:WebhookSecret"];
        if (string.IsNullOrEmpty(expected) || secret != expected)
            return Unauthorized(new { error = "Invalid or missing webhook secret." });

        // Fire-and-forget incremental re-index
        _ = Task.Run(() => _indexer.IndexRepositoryAsync(CancellationToken.None));

        return Accepted(new { message = "Re-indexing started." });
    }
}
