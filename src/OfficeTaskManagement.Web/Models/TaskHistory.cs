using System;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    public class TaskHistory
    {
        [Key]
        public int Id { get; set; }

        public int TaskItemId { get; set; }
        public TaskItem? TaskItem { get; set; }

        public string? ChangedById { get; set; }
        public User? ChangedBy { get; set; }

        /// <summary>
        /// The name of the field that was changed (e.g., "AssigneeId", "Status", "EstimatedHours").
        /// Enables structured filtering of the audit trail per field.
        /// </summary>
        public string? FieldChanged { get; set; }

        /// <summary>The value of the field before the change. Stored as string representation.</summary>
        public string? OldValue { get; set; }

        /// <summary>The value of the field after the change. Stored as string representation.</summary>
        public string? NewValue { get; set; }

        /// <summary>
        /// The RACI role held by the actor (ChangedBy user) at the time of this change.
        /// Critical for traceability: identifies whether the change was made by the
        /// Responsible (R), Accountable (A), or another role participant.
        /// </summary>
        public RaciRole? RaciRoleAtTime { get; set; }

        /// <summary>Human-readable summary kept for display. Auto-generated from field changes if null.</summary>
        public string ChangeDescription { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
