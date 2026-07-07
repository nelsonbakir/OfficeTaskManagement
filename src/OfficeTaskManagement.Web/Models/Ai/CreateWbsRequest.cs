using System.Collections.Generic;

namespace OfficeTaskManagement.Models.Ai
{
    public class WbsTaskDto
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public decimal OptimisticHours { get; set; }
        public decimal MostLikelyHours { get; set; }
        public decimal PessimisticHours { get; set; }
    }

    public class WbsStoryDto
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? AcceptanceCriteria { get; set; }
        public List<WbsTaskDto> Tasks { get; set; } = new();
    }

    public class WbsFeatureDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public List<WbsStoryDto> Stories { get; set; } = new();
    }

    public class WbsEpicDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public List<WbsFeatureDto> Features { get; set; } = new();
    }

    public class CreateWbsRequest
    {
        public int ProjectId { get; set; }
        public List<WbsEpicDto> Wbs { get; set; } = new();
    }

    public record AgentSessionDto(string Id, string Title, System.DateTimeOffset CreatedAt, System.DateTimeOffset UpdatedAt);
}
