using System.Threading.Tasks;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Core PMP Workflow Engine — drives the RACI task lifecycle.
    /// Responsible for spawning stage sub-tasks from templates (Fragnets),
    /// enforcing stage gate transitions, computing PERT estimates,
    /// rolling up parent status, and producing work package summaries.
    /// </summary>
    public interface IWorkflowEngineService
    {
        /// <summary>
        /// Spawns one child TaskItem per stage in the given WorkflowTemplate,
        /// all linked as sub-tasks of the parent. The parent is transformed into
        /// a Summary Task: RaciRole = Accountable, AssigneeId = null,
        /// IsWorkPackage = true. StartToStart stages activate immediately;
        /// LagHours are stored as PlannedStartDate on each sub-task.
        /// </summary>
        Task SpawnWorkflowSubTasksAsync(int parentTaskId, int templateId);

        /// <summary>
        /// Validates stage gate criteria via StageGateService, then marks the
        /// given sub-task as complete, activates the next stage (honouring LagHours
        /// and StageDependency), notifies the next assignee, and syncs the parent
        /// Work Package status via SyncParentStatusAsync.
        /// Throws InvalidOperationException if gate or RACI criteria are not met.
        /// </summary>
        Task TransitionStageAsync(int subTaskId, string actorUserId);

        /// <summary>
        /// Derives the parent Work Package status and baseline hours from the
        /// aggregate state of its stage sub-tasks (bottom-up roll-up).
        /// Should be called after any sub-task status or estimate change.
        /// </summary>
        Task SyncParentStatusAsync(int parentId, string? actorUserId = null);

        /// <summary>
        /// Computes the PERT weighted average: (Optimistic + 4*MostLikely + Pessimistic) / 6.
        /// </summary>
        decimal CalculatePert(decimal optimistic, decimal mostLikely, decimal pessimistic);

        /// <summary>
        /// Returns an aggregated summary of the entire work package:
        /// total PERT estimate, total actual hours, effort variance per stage,
        /// and time-in-status breakdown for reporting and PM review.
        /// </summary>
        Task<WorkPackageSummary> GetWorkPackageSummaryAsync(int parentTaskId);
    }
}
