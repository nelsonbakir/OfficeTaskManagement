namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Request DTO for re-estimating an existing entity using its original and actual hours.
    /// Used when a task is in progress and actual data differs from original estimate.
    /// </summary>
    public record ReEstimationRequest(
        string EntityType,
        string Title,
        string? Description,
        int EntityId,
        int? ProjectId,
        decimal OriginalPertHours,
        decimal? ActualHoursLogged,     // Hours spent so far
        string? ChangeReason            // Why re-estimation is needed
    );
}
