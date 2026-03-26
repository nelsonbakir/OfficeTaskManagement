using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Extended resource profile for a system User — stores capacity, cost, and skill data.
    /// Linked 1-to-1 with the User entity.
    /// </summary>
    public class ResourceProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        /// <summary>Department / team the resource belongs to.</summary>
        [StringLength(100)]
        public string? Department { get; set; }

        /// <summary>Seniority level of the resource.</summary>
        public SeniorityLevel SeniorityLevel { get; set; } = SeniorityLevel.Mid;

        /// <summary>Working hours available per day (default 8). Used as base capacity.</summary>
        [Range(0.5, 24)]
        public decimal DailyCapacityHours { get; set; } = 8;

        /// <summary>Billing / internal cost rate per hour. Visible to Manager role only.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal HourlyRate { get; set; } = 0;

        /// <summary>
        /// When true, this user is a schedulable team member and appears in capacity planning.
        /// When false, this user is a Stakeholder (e.g., external client, observer) and is
        /// excluded from utilization, heatmap, and allocation calculations.
        /// Default: true (PMBOK Guide, Ch. 9 Resource Management vs Ch. 13 Stakeholder Management).
        /// </summary>
        public bool IsResource { get; set; } = true;

        /// <summary>Optional freeform notes (e.g., preferred hours, location).</summary>
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<ResourceSkill> Skills { get; set; } = new List<ResourceSkill>();
        public ICollection<ProjectResourceAllocation> ProjectAllocations { get; set; } = new List<ProjectResourceAllocation>();
        public ICollection<ResourceAvailabilityBlock> AvailabilityBlocks { get; set; } = new List<ResourceAvailabilityBlock>();
    }
}
