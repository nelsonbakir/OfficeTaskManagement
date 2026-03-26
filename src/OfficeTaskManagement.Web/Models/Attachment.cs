using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class Attachment
    {
        [Key]
        public int Id { get; set; }

        public int? TaskItemId { get; set; }
        public TaskItem? TaskItem { get; set; }

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public int? EpicId { get; set; }
        public Epic? Epic { get; set; }

        public int? FeatureId { get; set; }
        public Feature? Feature { get; set; }

        public int? UserStoryId { get; set; }
        public UserStory? UserStory { get; set; }

        public int? TestCaseId { get; set; }
        public TestCase? TestCase { get; set; }

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        public string? UploadedById { get; set; }
        public User? UploadedBy { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
