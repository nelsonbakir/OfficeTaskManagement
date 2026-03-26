using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Audit log of every strategic management decision made from the Strategic Hub.
    /// Provides a chronological record of who decided what, when, and why.
    /// </summary>
    public class PortfolioDecision
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Type of decision: PauseProject, ResumeProject, DelayProject, AccelerateProject,
        /// PlanNewProject, CancelProject, PauseTask, ResumeTask, ReassignTask,
        /// MoveTaskSprint, AddPriorityTask, ShiftResources
        /// </summary>
        [Required]
        [StringLength(100)]
        public string DecisionType { get; set; } = string.Empty;

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public int? TaskId { get; set; }

        /// <summary>Free-text notes explaining the management rationale.</summary>
        public string? Notes { get; set; }

        /// <summary>Additional JSON metadata (e.g., old/new assignee, old/new sprint).</summary>
        public string? Metadata { get; set; }

        [Required]
        public string MadeById { get; set; } = string.Empty;
        public User? MadeBy { get; set; }

        public DateTime MadeAt { get; set; } = DateTime.UtcNow;
    }
}
