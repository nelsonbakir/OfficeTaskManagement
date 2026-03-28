using System.Threading.Tasks;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Core PMP Workflow Engine — drives the RACI task lifecycle.
    /// Responsible for spawning stage sub-tasks from templates (Fragnets),
    /// enforcing stage gate transitions, computing PERT estimates, and
    /// producing work package summaries for reporting.
    /// </summary>
    public interface IWorkflowEngineService
    {
        /// <summary>
        /// Spawns one child TaskItem per stage in the given WorkflowTemplate,
        /// all linked as sub-tasks of the parent. Each sub-task inherits the
        /// parent's Project/Sprint context and is tagged with its WorkflowStageId
        /// and RaciRole = Responsible. The Accountable user is propagated from
        /// the parent task's AccountableUserId to every child.
        /// </summary>
        /// <param name="parentTaskId">The parent feature/work-package TaskItem ID.</param>
        /// <param name="templateId">The WorkflowTemplate (Fragnet) to instantiate.</param>
        Task SpawnWorkflowSubTasksAsync(int parentTaskId, int templateId);

        /// <summary>
        /// Validates stage gate criteria via StageGateService, then marks the
        /// given sub-task as complete and activates the next stage sub-task
        /// (respecting LagHours and StageDependency type).
        /// Throws InvalidOperationException if gate criteria are not met.
        /// </summary>
        /// <param name="subTaskId">The currently active workflow sub-task ID.</param>
        /// <param name="actorUserId">The user triggering the transition (for audit trail).</param>
        Task TransitionStageAsync(int subTaskId, string actorUserId);

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
