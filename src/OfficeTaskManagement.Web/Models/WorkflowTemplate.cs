using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// A reusable, ordered pipeline of workflow stages (Fragnet in PMP terminology).
    /// Templates can be scoped to a specific project or defined globally (ProjectId = null).
    /// When a TaskItem is assigned a template, the WorkflowEngineService automatically
    /// spawns one child sub-task per stage, each with its own Responsible assignee and
    /// independent PERT-based effort estimate.
    /// </summary>
    public class WorkflowTemplate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// When null, this template is a global default available to all projects.
        /// When set, this template is specific to the referenced project.
        /// </summary>
        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>
        /// Maps to TaskType so different pipelines apply automatically per task type.
        /// e.g. Bug tasks may use a shorter pipeline that skips Code Review.
        /// Null means this template applies to all task types.
        /// </summary>
        public Enums.TaskType? ApplicableTaskType { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<WorkflowStage> Stages { get; set; } = new List<WorkflowStage>();
    }
}
