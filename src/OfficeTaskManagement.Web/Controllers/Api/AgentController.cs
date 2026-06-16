using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public AgentController(
        IConfiguration config,
        CodebaseIndexingService indexer,
        IAgentService agent)
    {
        _config  = config;
        _indexer = indexer;
        _agent   = agent;
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

        _ = Task.Run(() => _indexer.IndexRepositoryAsync(CancellationToken.None));
        return Accepted(new { message = "Re-indexing started." });
    }

    // POST /api/agent/chat
    // Multi-turn AI copilot chat endpoint.
    [HttpPost("chat")]
    [Authorize]
    public async Task<ActionResult<AgentChatResponse>> ChatAsync(
        [FromBody] AgentChatRequest request, CancellationToken ct)
    {
        // Enrich request with authenticated user ID
        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var enriched = request with { UserId = userId };

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
