namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Represents a reference to an entity mentioned in the copilot chat (using @type:name).
    /// </summary>
    public record MentionReference(string Type, string Id);
}
