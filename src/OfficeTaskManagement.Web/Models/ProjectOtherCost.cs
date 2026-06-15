using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Represents a single non-labour cost line item attached to a project.
    /// Used alongside resource-allocation labour costs to build a complete
    /// cost picture for forecasting and budget control.
    ///
    /// Examples: server licences, travel expenses, third-party service fees,
    /// hardware procurement, training courses.
    /// </summary>
    public class ProjectOtherCost : IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;

        [Key]
        public int Id { get; set; }

        // ── Project Link ─────────────────────────────────────────────────────

        [Required]
        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        // ── Cost Definition ──────────────────────────────────────────────────

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Categorisation for reporting roll-up.</summary>
        public OtherCostCategory Category { get; set; } = OtherCostCategory.Miscellaneous;

        /// <summary>Whether this is a one-time or recurring cost.</summary>
        public CostFrequency Frequency { get; set; } = CostFrequency.OneTime;

        // ── Amounts (BDT) ────────────────────────────────────────────────────

        /// <summary>Estimated (planned) amount in project currency (BDT).</summary>
        [Required]
        [Range(0, 999_999_999)]
        [Column(TypeName = "decimal(14,2)")]
        public decimal EstimatedAmount { get; set; }

        /// <summary>
        /// Actual amount incurred. Null until the cost is realised.
        /// Populated when the expense is confirmed/invoiced.
        /// </summary>
        [Range(0, 999_999_999)]
        [Column(TypeName = "decimal(14,2)")]
        public decimal? ActualAmount { get; set; }

        // ── Temporal ─────────────────────────────────────────────────────────

        /// <summary>When this cost is expected to be incurred (plan date).</summary>
        public DateTime? PlannedDate { get; set; }

        /// <summary>When the actual cost was realised/invoiced.</summary>
        public DateTime? ActualDate { get; set; }

        // ── Flags ────────────────────────────────────────────────────────────

        /// <summary>
        /// When true, this item is a contingency/reserve cost and is
        /// excluded from the cost baseline but included in the budget ceiling.
        /// </summary>
        public bool IsContingency { get; set; } = false;

        /// <summary>Optional free-text notes (vendor, PO number, approver, etc.).</summary>
        [StringLength(500)]
        public string? Notes { get; set; }

        // ── Audit ────────────────────────────────────────────────────────────

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
