using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    public class Project : IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;

        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? LogoPath { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Strategic Management Fields ──────────────────────────────────────
        /// <summary>Management-set strategic status (Active, OnHold, Delayed, etc.)</summary>
        public ProjectStrategicStatus StrategicStatus { get; set; } = ProjectStrategicStatus.Active;

        /// <summary>Management rationale recorded when strategic status changes.</summary>
        public string? StrategicStatusReason { get; set; }

        public DateTime? StrategicStatusChangedAt { get; set; }

        public string? StrategicStatusChangedById { get; set; }
        public User? StrategicStatusChangedBy { get; set; }

        /// <summary>ISO week number when this project is planned to start (for Planning status).</summary>
        public int? PlannedStartWeek { get; set; }

        /// <summary>Flagged for executive-level visibility on the Strategic Hub radar.</summary>
        public bool IsOnExecutiveRadar { get; set; } = false;

        /// <summary>Comma-separated list of required skills for resources allocated to this project.</summary>
        public string? RequiredSkills { get; set; }
        // ────────────────────────────────────────────────────────────────────

        public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
        public ICollection<Epic> Epics { get; set; } = new List<Epic>();
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
        public ICollection<PortfolioDecision> PortfolioDecisions { get; set; } = new List<PortfolioDecision>();

        // ── Resource Management ──────────────────────────────────────────────
        public ICollection<ProjectResourceAllocation> ResourceAllocations { get; set; } = new List<ProjectResourceAllocation>();
        // ────────────────────────────────────────────────────────────────────

        // ── Budget Management (PMBOK Cost Management, Ch. 7) ─────────────────
        /// <summary>How this project's budget was established (top-down, bottom-up, or not yet set).</summary>
        public BudgetMode BudgetMode { get; set; } = BudgetMode.NotSet;

        /// <summary>
        /// The formally approved cost baseline in BDT.
        /// Null when BudgetMode is NotSet or DerivedFromWork.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal? ApprovedBudget { get; set; }

        /// <summary>
        /// Contingency reserve in BDT (PMBOK 7.3 — known risks buffer).
        /// Included in the total budget ceiling but excluded from the cost baseline.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal? ContingencyReserve { get; set; }

        /// <summary>When the budget was formally set or last revised.</summary>
        public DateTime? BudgetSetAt { get; set; }

        /// <summary>Who set or last revised the budget.</summary>
        public string? BudgetSetById { get; set; }
        public User? BudgetSetBy { get; set; }

        /// <summary>Non-labour cost line items (hardware, licenses, travel, etc.).</summary>
        public ICollection<ProjectOtherCost> OtherCosts { get; set; } = new List<ProjectOtherCost>();
        // ────────────────────────────────────────────────────────────────────
    }
}
