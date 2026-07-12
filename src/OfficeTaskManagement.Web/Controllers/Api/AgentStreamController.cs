using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Agent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace OfficeTaskManagement.Controllers.Api;

/// <summary>
/// Streaming AI Copilot endpoint — KF-1 (Streaming).
/// POST /api/agent/chat/stream returns NDJSON where each line is
/// {"chunk":"..."} and the final line is {"done":true}.
/// The client reads these with a ReadableStream / fetch reader loop.
/// </summary>
[ApiController]
[Route("api/agent")]
public class AgentStreamController : ControllerBase
{
    private readonly IAgentService _agent;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AgentStreamController> _logger;

    public AgentStreamController(
        IAgentService agent,
        ApplicationDbContext db,
        ILogger<AgentStreamController> logger)
    {
        _agent  = agent;
        _db     = db;
        _logger = logger;
    }

    // POST /api/agent/chat/stream
    // Streaming multi-turn AI copilot endpoint — yields NDJSON chunks.
    [HttpPost("chat/stream")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task StreamChatAsync([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var tenantId = _db.CurrentTenantId;

        // Server-side enrich — client cannot spoof userId / tenantId
        var enriched = request with { UserId = userId, TenantId = tenantId };

        Response.Headers["Content-Type"]      = "application/x-ndjson; charset=utf-8";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable Nginx proxy buffering

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        try
        {
            await foreach (var obj in _agent.StreamChatAsync(enriched, ct))
            {
                var line  = JsonSerializer.Serialize(obj, options) + "\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                await Response.Body.WriteAsync(bytes, ct);
                await Response.Body.FlushAsync(ct);
            }

            // Write terminal sentinel so clients know the stream is complete
            var done  = JsonSerializer.Serialize(new { done = true }, options) + "\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(done), ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to do; response is already streaming
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentStreamController: unhandled error during streaming");
            try
            {
                var errLine = JsonSerializer.Serialize(new { chunk = "\n\n⚠ An error occurred. Please try again." }, options) + "\n";
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(errLine), ct);
                await Response.Body.FlushAsync(ct);
            }
            catch { /* swallow — response may already be closed */ }
        }
    }
}
