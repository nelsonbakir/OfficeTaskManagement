using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class Project
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
    }
}
