using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class TaskAttachment
    {
        [Key]
        public int Id { get; set; }

        public int TaskItemId { get; set; }
        public TaskItem? TaskItem { get; set; }

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
