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

        /// <summary>
        /// Scans and analyzes the repository structure and technology stack of a project,
        /// returning a project summary and a list of recommended Epics.
        /// </summary>
        Task<ProjectAnalysisResult> AnalyzeProjectCodebaseAsync(int projectId, CancellationToken ct = default);

        /// <summary>
        /// Suggests features for a given Epic, grounded in relevant codebase files/classes.
        /// </summary>
        Task<List<FeatureSuggestionDto>> SuggestFeaturesForEpicAsync(int projectId, string epicName, string epicDescription, CancellationToken ct = default);

        /// <summary>
        /// Suggests User Stories for a Feature, complete with acceptance criteria in Given/When/Then format.
        /// </summary>
        Task<List<UserStorySuggestionDto>> SuggestUserStoriesForFeatureAsync(int projectId, string epicName, string featureName, string featureDescription, CancellationToken ct = default);

        /// <summary>
        /// Suggests implementation tasks and test cases for a User Story.
        /// If suggestTests is true, focuses on creating comprehensive QA test cases and writing unit tests.
        /// </summary>
        Task<TaskAndTestCaseSuggestionsDto> SuggestTasksAndTestCasesAsync(int projectId, string storyTitle, string storyDescription, bool suggestTests, CancellationToken ct = default);
    }
}

