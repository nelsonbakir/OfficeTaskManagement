using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Agent;

/// <summary>
/// Persists and retrieves multi-turn conversation history for the AI Copilot sidebar.
/// Each conversation is keyed by conversationId, scoped to a user + PM entity.
/// Spec: ai-agent-plan/05_SERVICE_LAYER.md → AgentService (Phase 4)
/// </summary>
public class AgentConversationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AgentConversationService> _logger;

    public AgentConversationService(
        ApplicationDbContext db,
        ILogger<AgentConversationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns an existing conversation or creates a new one for the given user + entity context.
    /// </summary>
    public async Task<AgentConversation> GetOrCreateAsync(
        string conversationId,
        string userId,
        string tenantId,
        string? entityType,
        int? entityId,
        CancellationToken ct = default)
    {
        var conversation = await _db.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);

        if (conversation != null)
        {
            // Reset expiry on access
            conversation.ExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return conversation;
        }

        // Create new conversation
        conversation = new AgentConversation
        {
            Id         = conversationId,
            UserId     = userId,
            TenantId   = tenantId,
            EntityType = entityType,
            EntityId   = entityId,
            TurnsJson  = "[]",
            CreatedAt  = DateTimeOffset.UtcNow,
            UpdatedAt  = DateTimeOffset.UtcNow,
            ExpiresAt  = DateTimeOffset.UtcNow.AddHours(24)
        };
        _db.AgentConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    /// <summary>
    /// Appends a new turn (user or model) to the conversation's TurnsJson.
    /// </summary>
    public async Task AppendTurnAsync(
        string conversationId,
        string role,
        string text,
        CancellationToken ct = default)
    {
        var conversation = await _db.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation == null)
        {
            _logger.LogWarning("AppendTurnAsync: conversation {Id} not found", conversationId);
            return;
        }

        var turns = GetTurns(conversation.TurnsJson);
        turns.Add(new ConversationTurn(role, text, DateTimeOffset.UtcNow));

        // Cap history at 40 turns to avoid context window overflow (~20 user + 20 model)
        if (turns.Count > 40)
            turns = turns.TakeLast(40).ToList();

        conversation.TurnsJson = JsonSerializer.Serialize(turns);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        conversation.ExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns all turns in the conversation as a typed list.
    /// Used to build the Gemini `previous_turns` array.
    /// </summary>
    public async Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(
        string conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.AgentConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation == null) return Array.Empty<ConversationTurn>();
        return GetTurns(conversation.TurnsJson);
    }

    /// <summary>
    /// Gets all chat sessions (conversations) for a project context, ordered by last update.
    /// Dynamic titles are extracted from the first user turn.
    /// </summary>
    public async Task<List<AgentSessionDto>> GetProjectSessionsAsync(
        int projectId, string userId, CancellationToken ct = default)
    {
        var conversations = await _db.AgentConversations
            .AsNoTracking()
            .Where(c => c.EntityType == "Project" && c.EntityId == projectId && c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        var sessions = new List<AgentSessionDto>();
        foreach (var c in conversations)
        {
            string title = "";
            var turns = GetTurns(c.TurnsJson);
            var firstUserTurn = turns.Find(t => t.Role == "user");
            if (firstUserTurn != null && !string.IsNullOrWhiteSpace(firstUserTurn.Text))
            {
                title = firstUserTurn.Text.Length > 40
                    ? firstUserTurn.Text.Substring(0, 37) + "..."
                    : firstUserTurn.Text;
            }
            else
            {
                title = $"Session ({c.CreatedAt.ToLocalTime().ToString("g")})";
            }

            sessions.Add(new AgentSessionDto(c.Id, title, c.CreatedAt, c.UpdatedAt));
        }
        return sessions;
    }

    /// <summary>Deletes a conversation and all its turns.</summary>
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation != null)
        {
            _db.AgentConversations.Remove(conversation);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static List<ConversationTurn> GetTurns(string turnsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ConversationTurn>>(turnsJson)
                   ?? new List<ConversationTurn>();
        }
        catch
        {
            return new List<ConversationTurn>();
        }
    }
}

/// <summary>A single turn in a multi-turn conversation (user or model).</summary>
public record ConversationTurn(string Role, string Text, DateTimeOffset Timestamp);
