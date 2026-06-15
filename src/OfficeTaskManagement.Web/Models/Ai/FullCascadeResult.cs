namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Task-level item in a full cascade breakdown.
    /// </summary>
    public record CascadeTaskDto(
        string Title,
        decimal OptimisticHours,
        decimal MostLikelyHours,
        decimal PessimisticHours
    );

    /// <summary>
    /// User story in a full cascade breakdown, containing its child tasks.
    /// </summary>
    public record CascadeUserStoryDto(
        string Title,
        string Description,
        string AcceptanceCriteria,
        decimal MostLikelyHours,
        CascadeTaskDto[] Tasks
    );

    /// <summary>
    /// Feature in a full cascade breakdown, containing its child user stories.
    /// </summary>
    public record CascadeFeatureDto(
        string Title,
        string Description,
        CascadeUserStoryDto[] UserStories
    );

    /// <summary>
    /// Full cascade result for an Epic: Features → UserStories → Tasks in a single AI call.
    /// Used when the user clicks "Full Breakdown" on an Epic.
    /// Warning: can use 3,000–5,000 output tokens.
    /// </summary>
    public record FullCascadeResult(
        CascadeFeatureDto[] Features
    );

    /// <summary>
    /// Request for generating a full cascade breakdown of an Epic.
    /// </summary>
    public record FullCascadeRequest(
        string EpicTitle,
        string? EpicDescription,
        int? ProjectId,
        int? EpicId,
        string? ProjectName
    );
}
