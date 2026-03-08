using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    public class UserStory
    {
        [Key]
        public int Id { get; set; }

        public int FeatureId { get; set; }
        public Feature? Feature { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? AcceptanceCriteria { get; set; }

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
        public ICollection<TestCase> TestCases { get; set; } = new List<TestCase>();
    }
}
