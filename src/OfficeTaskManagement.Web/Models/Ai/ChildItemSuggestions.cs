namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// DTO for a single suggested child item returned by the AI.
    /// Used when suggesting Features for an Epic, UserStories for a Feature, etc.
    /// </summary>
    public record ChildItemDto(
        string Title,
        string Description,
        decimal? OptimisticHours,
        decimal? MostLikelyHours,
        decimal? PessimisticHours,
        string Priority,                 // "Low" | "Medium" | "High" | "Critical"
        string? AcceptanceCriteria       // Only populated for UserStory children
    );

    /// <summary>
    /// Collection of AI-suggested child items for a parent entity.
    /// </summary>
    public record ChildItemSuggestions(
        string ParentType,
        string ChildType,
        ChildItemDto[] Items,
        string Rationale
    );
}
