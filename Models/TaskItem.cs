using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    public class TaskItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.New;

        public TaskType Type { get; set; } = TaskType.NewRequest;

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        public bool IsBacklog { get; set; } = false;

        // Efforts required in hours
        public decimal EstimatedHours { get; set; }

        // Starting date of the task
        public DateTime? StartDate { get; set; }

        public DateTime? DueDate { get; set; }

        public int? SprintId { get; set; }
        public Sprint? Sprint { get; set; }

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public int? FeatureId { get; set; }
        public Feature? Feature { get; set; }

        public int? UserStoryId { get; set; }
        public UserStory? UserStory { get; set; }

        public ICollection<Area> Areas { get; set; } = new List<Area>();

        public string? AssigneeId { get; set; }
        public User? Assignee { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ParentTaskId { get; set; }
        public TaskItem? ParentTask { get; set; }

        public ICollection<TaskItem> SubTasks { get; set; } = new List<TaskItem>();

        public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
        public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
    }
}
