using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Represents a formal allocation of a User to a Project at a defined capacity percentage.
    /// Multiple allocations per user across different projects are possible.
    /// </summary>
    public class ProjectResourceAllocation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        /// <summary>Percentage of this user's working capacity allocated to the project (0–100).</summary>
        [Required]
        [Range(1, 100)]
        public int AllocationPercentage { get; set; } = 100;

        /// <summary>Project-specific role title for this resource (e.g., "Dev Lead", "QA Engineer").</summary>
        [StringLength(100)]
        public string? ProjectRole { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        /// <summary>Null means open-ended / for the project's duration.</summary>
        public DateTime? EndDate { get; set; }

        public string? AllocatedById { get; set; }
        public User? AllocatedBy { get; set; }

        public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;

        // Link to resource profile for convenience
        public int? ResourceProfileId { get; set; }
        public ResourceProfile? ResourceProfile { get; set; }
    }
}
