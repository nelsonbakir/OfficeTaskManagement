using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class Sprint
    {
        [Key]
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
