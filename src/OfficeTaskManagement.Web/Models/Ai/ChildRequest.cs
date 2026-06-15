namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Request DTO for suggesting child items under a parent entity.
    /// E.g., suggest Features under an Epic, or Tasks under a UserStory.
    /// </summary>
    public record ChildRequest(
        string ParentType,          // "Epic" | "Feature" | "UserStory"
        string ChildType,           // "Feature" | "UserStory" | "Task"
        string ParentTitle,
        string? ParentDescription,
        int? ProjectId,
        int? EpicId,
        int? FeatureId,
        int? UserStoryId,
        int MinChildren = 3,
        int MaxChildren = 6
    );
}
