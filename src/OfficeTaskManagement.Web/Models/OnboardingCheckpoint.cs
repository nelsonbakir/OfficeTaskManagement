using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Tracks per-project onboarding wizard progress so the user can resume
    /// exactly where they left off after a browser refresh or session drop.
    /// </summary>
    public class OnboardingCheckpoint : IMustHaveTenant
    {
        [Key]
        public int Id { get; set; }

        /// <summary>FK to the project being onboarded.</summary>
        [Required]
        public int ProjectId { get; set; }

        [ForeignKey(nameof(ProjectId))]
        public Project? Project { get; set; }

        /// <summary>The highest step the user has successfully confirmed (1–6).</summary>
        public int LastCompletedStep { get; set; } = 0;

        /// <summary>True once the user clicks "Confirm &amp; Initiate Project".</summary>
        public bool IsCompleted { get; set; } = false;

        /// <summary>TenantId for multi-tenant filtering.</summary>
        [Required]
        public string TenantId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
