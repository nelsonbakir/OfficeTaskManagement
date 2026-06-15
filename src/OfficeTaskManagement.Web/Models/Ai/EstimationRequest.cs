namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Request DTO for AI estimation of any PM entity.
    /// </summary>
    public record EstimationRequest(
        string EntityType,          // "Project" | "Epic" | "Feature" | "UserStory" | "Task"
        string Title,
        string? Description,
        int? ProjectId,
        int? EpicId,
        int? FeatureId,
        int? UserStoryId
    );
}
