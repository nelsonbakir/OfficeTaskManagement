namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// A single item to be bulk-created by the AI one-click creation feature.
    /// </summary>
    public record BulkCreateItemDto(
        string EntityType,          // "Epic" | "Feature" | "UserStory" | "Task"
        int ParentId,               // The ID of the parent entity
        string Title,
        string? Description,
        string? AcceptanceCriteria, // For UserStory items
        string Priority,
        decimal? OptimisticHours,
        decimal? MostLikelyHours,
        decimal? PessimisticHours
    );

    /// <summary>
    /// Request for bulk-creating multiple AI-suggested items in a single transaction.
    /// </summary>
    public record BulkCreateRequest(
        BulkCreateItemDto[] Items
    );

    /// <summary>
    /// Result of a bulk-create operation containing the IDs of all created entities.
    /// </summary>
    public record BulkCreateResult(
        int[] CreatedIds,
        string EntityType,          // All created items are the same entity type
        string Message
    );
}
