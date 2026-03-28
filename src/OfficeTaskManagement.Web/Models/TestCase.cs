using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class TestCase
    {
        [Key]
        public int Id { get; set; }

        public int UserStoryId { get; set; }
        public UserStory? UserStory { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Steps { get; set; } = string.Empty;

        [Required]
        public string ExpectedResult { get; set; } = string.Empty;

        public bool IsAutomated { get; set; } = false;

        /// <summary>Set by the QA Responsible party after execution. Required by the QA Stage Gate.</summary>
        public bool IsPassed { get; set; } = false;

        /// <summary>Actual result recorded by QA during test execution.</summary>
        public string? ActualResult { get; set; }

        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    }
}
