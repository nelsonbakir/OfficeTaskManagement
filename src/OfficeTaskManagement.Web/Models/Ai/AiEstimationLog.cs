using System;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Audit log for all AI estimation calls.
    /// Used for cost monitoring (token usage) and quality analysis
    /// (compare AI estimates vs actual hours after task completion).
    /// </summary>
    public class AiEstimationLog : IMustHaveTenant
    {
        [Key]
        public int Id { get; set; }

        public string TenantId { get; set; } = string.Empty;

        /// <summary>Who triggered the estimation.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>"Epic" | "Feature" | "UserStory" | "Task" | "Project"</summary>
        [StringLength(50)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>Null for new items (not yet saved), set for re-estimations.</summary>
        public int? EntityId { get; set; }

        [StringLength(100)]
        public string Model { get; set; } = string.Empty;

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }

        /// <summary>AI-returned PERT estimate at the time of call.</summary>
        public decimal? AiPertHours { get; set; }

        /// <summary>
        /// Populated retroactively via a background job:
        /// actual hours from TaskHistory once the entity reaches Done.
        /// Enables "AI accuracy" analytics dashboard.
        /// </summary>
        public decimal? ActualHours { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
