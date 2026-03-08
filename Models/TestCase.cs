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
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    }
}
