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

        // ── Resource / Capacity Planning ─────────────────────────────────────
        /// <summary>Manager-set team capacity ceiling for this sprint in hours.</summary>
        public decimal? PlannedCapacityHours { get; set; }

        /// <summary>Free-text notes about resource constraints or sprint goals.</summary>
        public string? TeamNotes { get; set; }
        // ────────────────────────────────────────────────────────────────────

        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
