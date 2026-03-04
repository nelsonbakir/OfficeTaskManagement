using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class TaskHistory
    {
        [Key]
        public int Id { get; set; }

        public int TaskItemId { get; set; }
        public TaskItem? TaskItem { get; set; }

        public string? ChangedById { get; set; }
        public User? ChangedBy { get; set; }

        public string ChangeDescription { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
