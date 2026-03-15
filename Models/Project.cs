using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

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
        // ────────────────────────────────────────────────────────────────────

        public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
        public ICollection<Epic> Epics { get; set; } = new List<Epic>();
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
        public ICollection<PortfolioDecision> PortfolioDecisions { get; set; } = new List<PortfolioDecision>();
    }
}
