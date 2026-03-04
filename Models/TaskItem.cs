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

        public TaskStatus Status { get; set; } = TaskStatus.ToDo;

        // Efforts required in hours
        public decimal EstimatedHours { get; set; }

        // Starting date of the task
        public DateTime? StartDate { get; set; }

        public DateTime? DueDate { get; set; }

        public int? SprintId { get; set; }
        public Sprint? Sprint { get; set; }

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public string? AssigneeId { get; set; }
        public User? Assignee { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
        public ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
    }
}
