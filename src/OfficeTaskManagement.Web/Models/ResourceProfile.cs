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
    public class ResourceProfile : IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;

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

        // ── Resource Classification ──────────────────────────────────────────

        /// <summary>
        /// How the resource is engaged (Full-Time, Part-Time, Contractual, etc.).
        /// Drives salary-type defaults and UI presentation.
        /// </summary>
        public ResourceType ResourceType { get; set; } = ResourceType.FullTime;

        // ── Salary Snapshot (denormalised from SalaryHistory for fast reads) ─

        /// <summary>
        /// How the current compensation amount is expressed.
        /// Source of truth is the active SalaryHistory record; this field is
        /// kept in sync by ResourceService.RecordSalaryChangeAsync.
        /// </summary>
        public SalaryType CurrentSalaryType { get; set; } = SalaryType.MonthlySalary;

        /// <summary>
        /// The raw compensation amount in <see cref="Currency"/> units,
        /// per the period defined by <see cref="CurrentSalaryType"/>.
        /// </summary>
        [Column(TypeName = "decimal(14,2)")]
        public decimal CurrentSalaryAmount { get; set; } = 0;

        /// <summary>ISO 4217 currency code (default: BDT).</summary>
        [StringLength(3)]
        public string Currency { get; set; } = "BDT";

        // ────────────────────────────────────────────────────────────────────

        /// <summary>Working hours available per day (default 8). Used as base capacity.</summary>
        [Range(0.5, 24)]
        public decimal DailyCapacityHours { get; set; } = 8;

        /// <summary>
        /// Cached effective hourly cost rate. Visible to Manager/Admin roles only.
        /// Always kept in sync with the active SalaryHistory.EffectiveHourlyRate.
        /// Do NOT update this directly — use ResourceService.RecordSalaryChangeAsync.
        /// </summary>
        [Column(TypeName = "decimal(10,4)")]
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

        /// <summary>Full temporal salary ledger — ordered by EffectiveFrom desc in queries.</summary>
        public ICollection<SalaryHistory> SalaryHistories { get; set; } = new List<SalaryHistory>();
    }
}
