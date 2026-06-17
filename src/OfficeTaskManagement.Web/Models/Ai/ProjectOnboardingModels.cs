using System.Collections.Generic;

namespace OfficeTaskManagement.Models.Ai
{
    public record ProjectAnalysisResult(
        string ProjectSummary,
        string TechStack,
        string TestOverview,
        bool TestsAbsentOrIncomplete,
        EpicSuggestionDto[] SuggestedEpics
    );

    public record EpicSuggestionDto(string Name, string Description);
    public record FeatureSuggestionDto(string Name, string Description);
    public record UserStorySuggestionDto(string Title, string Description, string AcceptanceCriteria, string Priority);
    
    public record TaskAndTestCaseSuggestionsDto(
        TaskSuggestionDto[] Tasks,
        TestCaseSuggestionDto[] TestCases
    );

    public record TaskSuggestionDto(
        string Title,
        string Description,
        decimal OptimisticHours,
        decimal MostLikelyHours,
        decimal PessimisticHours,
        string Priority
    );

    public record TestCaseSuggestionDto(
        string Title,
        string Steps,
        string ExpectedResult
    );

    // Confirmation Request Payload
    public class OnboardProjectRequest
    {
        public int ProjectId { get; set; }
        public List<OnboardEpicDto> Epics { get; set; } = new();
    }

    public class OnboardEpicDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<OnboardFeatureDto> Features { get; set; } = new();
    }

    public class OnboardFeatureDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<OnboardUserStoryDto> UserStories { get; set; } = new();
    }

    public class OnboardUserStoryDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AcceptanceCriteria { get; set; }
        public string Priority { get; set; } = "Medium";
        public List<OnboardTaskDto> Tasks { get; set; } = new();
        public List<OnboardTestCaseDto> TestCases { get; set; } = new();
    }

    public class OnboardTaskDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Priority { get; set; } = "Medium";
        public decimal OptimisticHours { get; set; }
        public decimal MostLikelyHours { get; set; }
        public decimal PessimisticHours { get; set; }
    }

    public class OnboardTestCaseDto
    {
        public string Title { get; set; } = string.Empty;
        public string Steps { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
    }

    public class SaveEpicsRequest
    {
        public int ProjectId { get; set; }
        public List<EpicSaveDto> Epics { get; set; } = new();
    }

    public class EpicSaveDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class SaveFeaturesRequest
    {
        public int EpicId { get; set; }
        public List<FeatureSaveDto> Features { get; set; } = new();
    }

    public class FeatureSaveDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class SaveStoriesRequest
    {
        public int FeatureId { get; set; }
        public List<StorySaveDto> Stories { get; set; } = new();
    }

    public class StorySaveDto
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AcceptanceCriteria { get; set; }
        public string Priority { get; set; } = "Medium";
    }

    public class SaveTasksAndTestsRequest
    {
        public int StoryId { get; set; }
        public List<TaskSaveDto> Tasks { get; set; } = new();
        public List<TestCaseSaveDto> TestCases { get; set; } = new();
    }

    public class TaskSaveDto
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Priority { get; set; } = "Medium";
        public decimal OptimisticHours { get; set; }
        public decimal MostLikelyHours { get; set; }
        public decimal PessimisticHours { get; set; }
    }

    public class TestCaseSaveDto
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Steps { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
    }
}
