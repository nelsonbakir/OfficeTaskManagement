using System.Threading;
using System.Threading.Tasks;
using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Onboarding
{
    /// <summary>
    /// Orchestrates all AI-analysis operations for the codebase-first onboarding wizard.
    /// Each method calls Gemini, then upserts the results into the DB in a transaction.
    /// </summary>
    public interface IOnboardingOrchestrationService
    {
        /// <summary>Step 2: Analyze the full project codebase and produce epic suggestions.</summary>
        Task<ProjectAnalysisResult> AnalyzeProjectAsync(int projectId, CancellationToken ct = default);

        /// <summary>Step 3: For a single epic, discover its constituent features via Gemini.</summary>
        Task<FeatureAnalysisResult> AnalyzeFeaturesForEpicAsync(int epicId, CancellationToken ct = default);

        /// <summary>Step 4: For a single feature, generate user stories with acceptance criteria.</summary>
        Task<StoryAnalysisResult> AnalyzeStoriesForFeatureAsync(int featureId, CancellationToken ct = default);

        /// <summary>Step 5: For a single story, generate implementation tasks + test cases with PERT.</summary>
        Task<TasksAndTestsAnalysisResult> AnalyzeTasksAndTestsForStoryAsync(int storyId, CancellationToken ct = default);

        // ── Save (user-confirmed) operations ──────────────────────────────────

        Task<SaveEpicsResponse> SaveEpicsAsync(SaveEpicsRequest request, string userId, CancellationToken ct = default);
        Task<SaveFeaturesResponse> SaveFeaturesAsync(SaveFeaturesRequest request, string userId, CancellationToken ct = default);
        Task<SaveStoriesResponse> SaveStoriesAsync(SaveStoriesRequest request, string userId, CancellationToken ct = default);
        Task<SaveTasksResponse> SaveTasksAndTestsAsync(SaveTasksAndTestsRequest request, string userId, CancellationToken ct = default);

        /// <summary>Step 6 completion: activates the project and marks onboarding done.</summary>
        Task CompleteOnboardingAsync(int projectId, string userId, CancellationToken ct = default);
    }

    // ── Thin result wrappers returned to the controller ──────────────────────

    public record FeatureAnalysisResult(int EpicId, string EpicName, object[] Features);
    public record StoryAnalysisResult(int FeatureId, string FeatureName, object[] Stories);
    public record TasksAndTestsAnalysisResult(int StoryId, string StoryTitle, object[] Tasks, object[] TestCases);

    public record SaveEpicsResponse(object[] Epics);
    public record SaveFeaturesResponse(int EpicId, object[] Features);
    public record SaveStoriesResponse(int FeatureId, object[] Stories);
    public record SaveTasksResponse(int StoryId, object[] Tasks, object[] TestCases);
}
