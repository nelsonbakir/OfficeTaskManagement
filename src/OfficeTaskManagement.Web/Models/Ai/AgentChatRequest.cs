namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Suggested action the AI wants to perform, returned by the copilot sidebar.
    /// The UI renders confirmation buttons for each action.
    /// </summary>
    public record AgentAction(
        string Type,        // "create_epic" | "create_feature" | "create_user_story" | "create_task"
        string Label,       // "Create Epic: Authentication"
        object Payload      // The data needed to create the entity
    );

    /// <summary>
    /// Request to the agentic copilot sidebar for a conversational turn.
    /// </summary>
    public record AgentChatRequest(
        string ConversationId,
        string UserId,
        string Message,
        string? EntityType,     // Current page context (e.g., "Epic")
        int? EntityId,          // Current page entity ID
        string TenantId = ""   // Server-side overwritten from ClaimsPrincipal — client sends empty
    );

    /// <summary>
    /// Response from the agentic copilot containing the AI message and optional action buttons.
    /// </summary>
    public record AgentChatResponse(
        string ConversationId,
        string Message,             // Markdown-formatted response text
        AgentAction[]? Actions      // Suggested actions to confirm (CreateEpic, CreateFeature, etc.)
    );
}
