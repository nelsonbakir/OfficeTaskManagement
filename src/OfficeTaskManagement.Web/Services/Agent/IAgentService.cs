using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Agent;

/// <summary>
/// Contract for the multi-turn AI Copilot service.
/// Spec: ai-agent-plan/05_SERVICE_LAYER.md → AgentService (Phase 4)
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// Processes a user message in a multi-turn conversation.
    /// Handles Gemini function calling agentic loop internally.
    /// </summary>
    Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken ct = default);

    /// <summary>Deletes all turns in a conversation, resetting it to empty.</summary>
    Task ClearConversationAsync(string conversationId, string userId, CancellationToken ct = default);
}
