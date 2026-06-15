using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Core AI service for estimation, child suggestion, and cascade generation
    /// using the Gemini API. Implements fallback behavior for missing API keys
    /// or API failures to ensure the application remains functional.
    /// </summary>
    public interface IGeminiAiService
    {
        /// <summary>
        /// Estimates effort for a PM entity (Task, UserStory, Feature, Epic, Project)
        /// using PERT three-point estimation via Gemini.
        /// Returns a fallback with Confidence="Low" if the API is unavailable.
        /// </summary>
        Task<EstimationResult> EstimateAsync(EstimationRequest request, CancellationToken ct = default);

        /// <summary>
        /// Suggests child items for a parent entity (e.g., Features for an Epic,
        /// Tasks for a UserStory). Used to power the one-click create UI.
        /// </summary>
        Task<ChildItemSuggestions> SuggestChildrenAsync(ChildRequest request, CancellationToken ct = default);

        /// <summary>
        /// Generates acceptance criteria for a UserStory given its title and description.
        /// Returns markdown-formatted text.
        /// </summary>
        Task<string> GenerateAcceptanceCriteriaAsync(string title, string description, CancellationToken ct = default);

        /// <summary>
        /// Re-estimates an existing entity taking into account actual hours logged
        /// and any scope drift detected since the original estimate.
        /// </summary>
        Task<EstimationResult> ReEstimateAsync(ReEstimationRequest request, CancellationToken ct = default);

        /// <summary>
        /// Generates a full cascade breakdown: Epic → Features → UserStories → Tasks.
        /// Warning: this can consume 3,000–5,000 output tokens. Only call for Epics with
        /// fewer than 5 existing features.
        /// </summary>
        Task<FullCascadeResult> GenerateFullCascadeAsync(FullCascadeRequest request, CancellationToken ct = default);
    }
}
