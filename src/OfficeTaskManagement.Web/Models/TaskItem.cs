using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    public class TaskItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.New;

        public TaskType Type { get; set; } = TaskType.NewRequest;

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        public bool IsBacklog { get; set; } = false;

        // ── RACI Role Classification ─────────────────────────────────────────
        /// <summary>
        /// The RACI role this task represents in its workflow stage.
        /// Sub-tasks spawned by the WorkflowEngine are always Responsible (R).
        /// The parent task retains the Accountable (A) designation.
        /// </summary>
        public RaciRole RaciRole { get; set; } = RaciRole.Responsible;

        /// <summary>FK to the WorkflowStage this sub-task represents. Null for standalone tasks.</summary>
        public int? WorkflowStageId { get; set; }
        public WorkflowStage? WorkflowStage { get; set; }

        /// <summary>
        /// The Accountable (A) person — typically the PM or Tech Lead.
        /// Remains fixed for the lifetime of the work package even as the
        /// Responsible (R) assignee changes between stages.
        /// </summary>
        public string? AccountableUserId { get; set; }
        public User? AccountableUser { get; set; }
        // ────────────────────────────────────────────────────────────────────

        // ── Effort Estimation (PMP Three-Point / PERT) ───────────────────────
        /// <summary>PM-approved baseline effort in hours (summed from stage PERT estimates).</summary>
        public decimal EstimatedHours { get; set; }

        /// <summary>Optimistic estimate (O) from the Responsible party for this stage.</summary>
        public decimal? EstimatedOptimisticHours { get; set; }

        /// <summary>Most Likely estimate (M) from the Responsible party for this stage.</summary>
        public decimal? EstimatedMostLikelyHours { get; set; }

        /// <summary>Pessimistic estimate (P) from the Responsible party for this stage.</summary>
        public decimal? EstimatedPessimisticHours { get; set; }

        /// <summary>
        /// PERT calculated estimate stored for reporting: (O + 4M + P) / 6.
        /// Recomputed and persisted whenever O, M, or P values change.
        /// </summary>
        public decimal? PertEstimatedHours { get; set; }

        /// <summary>Actual hours logged by the Responsible party upon stage completion.</summary>
        public decimal? ActualHours { get; set; }
        // ────────────────────────────────────────────────────────────────────

        // Starting date of the task
        public DateTime? StartDate { get; set; }

        public DateTime? DueDate { get; set; }

        public int? SprintId { get; set; }
        public Sprint? Sprint { get; set; }

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }
        
        public int? EpicId { get; set; }
        public Epic? Epic { get; set; }

        public int? FeatureId { get; set; }
        public Feature? Feature { get; set; }

        public int? UserStoryId { get; set; }
        public UserStory? UserStory { get; set; }

        public ICollection<Area> Areas { get; set; } = new List<Area>();

        public string? AssigneeId { get; set; }
        public User? Assignee { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ParentTaskId { get; set; }
        public TaskItem? ParentTask { get; set; }

        public ICollection<TaskItem> SubTasks { get; set; } = new List<TaskItem>();

        public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
        public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();

        // ── Strategic Management Fields ──────────────────────────────────────
        /// <summary>Manager-set pause flag. Task remains in its status but is flagged as blocked by decision.</summary>
        public bool IsPaused { get; set; } = false;

        public string? PauseReason { get; set; }

        public DateTime? PausedAt { get; set; }

        public string? PausedById { get; set; }
        public User? PausedBy { get; set; }
        // ────────────────────────────────────────────────────────────────────
    }
}
