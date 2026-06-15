namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Result of an AI estimation call, containing PERT three-point estimates,
    /// priority, story points, budget, confidence level, rationale, and risks.
    /// Also tracks token usage for cost monitoring.
    /// </summary>
    public record EstimationResult(
        decimal OptimisticHours,
        decimal MostLikelyHours,
        decimal PessimisticHours,
        decimal PertHours,
        string Priority,            // "Low" | "Medium" | "High" | "Critical"
        int StoryPoints,            // Fibonacci: 1,2,3,5,8,13,21
        decimal EstimatedBudgetBDT,
        string Confidence,          // "High" | "Medium" | "Low"
        string Rationale,
        string[] Risks,
        int InputTokensUsed,        // For cost monitoring
        int OutputTokensUsed
    );
}
