using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Represents one step in a WorkflowTemplate pipeline (e.g., "Development", "Code Review", "QA Testing").
    /// Each stage defines:
    /// - Which RACI role is Responsible for work at this step.
    /// - The dependency type to its predecessor (Finish-to-Start or Start-to-Start).
    /// - An optional lag (delay hours) before this stage activates after its predecessor.
    /// When the WorkflowEngine spawns sub-tasks, one TaskItem is created per stage.
    /// </summary>
    public class WorkflowStage
    {
        [Key]
        public int Id { get; set; }

        public int WorkflowTemplateId { get; set; }
        public WorkflowTemplate? WorkflowTemplate { get; set; }

        /// <summary>
        /// Display name of this stage (e.g., "Development", "Peer Code Review", "QA Testing").
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Execution order within the template. Lower numbers execute first.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// The RACI role that is Responsible (R) at this stage.
        /// Always Responsible — sub-tasks are the work units, not the oversight roles.
        /// </summary>
        public RaciRole RaciRole { get; set; } = RaciRole.Responsible;

        /// <summary>
        /// Human-readable label for the role performing this stage (e.g., "Developer", "Tech Lead", "QA Engineer").
        /// Used for display and to guide the PM when assigning the spawned sub-task.
        /// </summary>
        [StringLength(100)]
        public string DefaultRoleTitle { get; set; } = string.Empty;

        /// <summary>
        /// Dependency relationship to the predecessor stage (PMP PDM logic).
        /// Default is FinishToStart (hard logic — predecessor must finish before this starts).
        /// </summary>
        public StageDependency DependencyType { get; set; } = StageDependency.FinishToStart;

        /// <summary>
        /// Lag in hours: delay added between predecessor stage completion and this stage activation.
        /// Supports PMP Lead/Lag scheduling. Negative values represent Lead (early start).
        /// </summary>
        public decimal LagHours { get; set; } = 0;

        /// <summary>
        /// Definition of Done criteria text for this stage. The StageGateService enforces
        /// programmatic rules, but this text is displayed to the Responsible party as guidance.
        /// </summary>
        public string? DefinitionOfDone { get; set; }
    }
}
