using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Concrete implementation of the PMP Workflow Engine.
    /// Drives the RACI task lifecycle: spawns stage sub-tasks from Fragnet templates,
    /// enforces stage gate transitions, computes PERT estimates, and produces
    /// work package summaries for PM reporting.
    /// </summary>
    public class WorkflowEngineService : IWorkflowEngineService
    {
        private readonly ApplicationDbContext _db;
        private readonly StageGateService _gate;

        public WorkflowEngineService(ApplicationDbContext db, StageGateService gate)
        {
            _db = db;
            _gate = gate;
        }

        // ── PERT Formula ────────────────────────────────────────────────────────
        /// <inheritdoc/>
        public decimal CalculatePert(decimal optimistic, decimal mostLikely, decimal pessimistic)
            => (optimistic + (4 * mostLikely) + pessimistic) / 6;

        // ── Spawn Sub-Tasks from Template (Fragnet Instantiation) ───────────────
        /// <inheritdoc/>
        public async Task SpawnWorkflowSubTasksAsync(int parentTaskId, int templateId)
        {
            var parent = await _db.Tasks
                .FirstOrDefaultAsync(t => t.Id == parentTaskId)
                ?? throw new InvalidOperationException($"Parent task {parentTaskId} not found.");

            var template = await _db.WorkflowTemplates
                .Include(wt => wt.Stages)
                .FirstOrDefaultAsync(wt => wt.Id == templateId)
                ?? throw new InvalidOperationException($"WorkflowTemplate {templateId} not found.");

            // Remove any previously spawned sub-tasks for this template to avoid duplicates
            var existing = await _db.Tasks
                .Where(t => t.ParentTaskId == parentTaskId && t.WorkflowStageId != null)
                .ToListAsync();
            _db.Tasks.RemoveRange(existing);

            var stages = template.Stages.OrderBy(s => s.Order).ToList();
            TaskItem? previousSubTask = null;

            foreach (var stage in stages)
            {
                var subTask = new TaskItem
                {
                    Title           = $"[{stage.Name}] {parent.Title}",
                    Description     = stage.DefinitionOfDone,
                    Status          = TaskStatus.New,           // Inactive until predecessor completes
                    Type            = parent.Type,
                    Priority        = parent.Priority,
                    ProjectId       = parent.ProjectId,
                    SprintId        = parent.SprintId,
                    EpicId          = parent.EpicId,
                    FeatureId       = parent.FeatureId,
                    UserStoryId     = parent.UserStoryId,
                    ParentTaskId    = parentTaskId,
                    WorkflowStageId = stage.Id,
                    RaciRole        = RaciRole.Responsible,
                    AccountableUserId = parent.AccountableUserId ?? parent.CreatedById,
                    CreatedById     = parent.CreatedById,
                    CreatedAt       = DateTime.UtcNow,
                    IsBacklog       = false
                };

                // First stage activates immediately (ToDo); rest stay New until gate passes
                if (previousSubTask == null)
                    subTask.Status = TaskStatus.ToDo;

                _db.Tasks.Add(subTask);
                previousSubTask = subTask;
            }

            await _db.SaveChangesAsync();

            // Write structured audit entry on parent
            await WriteAuditAsync(
                parentTaskId,
                actorUserId: parent.CreatedById,
                field:       "WorkflowTemplate",
                oldValue:    null,
                newValue:    template.Name,
                raciRole:    RaciRole.Accountable,
                description: $"Workflow template '{template.Name}' applied. {stages.Count} stage sub-tasks spawned.");
        }

        // ── Stage Gate Transition ───────────────────────────────────────────────
        /// <inheritdoc/>
        public async Task TransitionStageAsync(int subTaskId, string actorUserId)
        {
            var current = await _db.Tasks
                .Include(t => t.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == subTaskId)
                ?? throw new InvalidOperationException($"Sub-task {subTaskId} not found.");

            // Enforce RACI-based stage gate — throws if criteria or permissions not met
            await _gate.EnforceGateAsync(current, actorUserId);

            // Determine local RACI role of the actor for audit purposes
            var actorRaciRole = actorUserId == current.AccountableUserId 
                ? RaciRole.Accountable 
                : RaciRole.Responsible;

            // Find the next sibling sub-task in order
            var nextSubTask = await _db.Tasks
                .Include(t => t.WorkflowStage)
                .Where(t =>
                    t.ParentTaskId == current.ParentTaskId &&
                    t.WorkflowStageId != null &&
                    t.WorkflowStage!.Order > current.WorkflowStage!.Order)
                .OrderBy(t => t.WorkflowStage!.Order)
                .FirstOrDefaultAsync();

            var oldStatus = current.Status.ToString();

            // Mark current stage complete
            current.Status = TaskStatus.Done;
            await _db.SaveChangesAsync();

            await WriteAuditAsync(subTaskId, actorUserId,
                field:       "Status",
                oldValue:    oldStatus,
                newValue:    TaskStatus.Done.ToString(),
                raciRole:    actorRaciRole,
                description: $"Stage '{current.WorkflowStage?.Name}' completed. Gate passed by {actorRaciRole}.");

            if (nextSubTask != null)
            {
                // Apply lag if defined (FinishToStart logic — immediate activation by default)
                var lag = nextSubTask.WorkflowStage?.LagHours ?? 0;
                
                if (lag <= 0)
                {
                    nextSubTask.Status = TaskStatus.ToDo;
                    await _db.SaveChangesAsync();

                    await WriteAuditAsync(nextSubTask.Id, actorUserId,
                        field:       "Status",
                        oldValue:    TaskStatus.New.ToString(),
                        newValue:    TaskStatus.ToDo.ToString(),
                        raciRole:    RaciRole.Accountable, // Next stage activation is an oversight/planning event
                        description: $"Stage '{nextSubTask.WorkflowStage?.Name}' activated automatically after predecessor gate passed.");
                }
            }
            else
            {
                // All stages complete — close the parent work package
                var parent = await _db.Tasks.FindAsync(current.ParentTaskId);
                if (parent != null)
                {
                    var oldParentStatus = parent.Status.ToString();
                    parent.Status = TaskStatus.Done;
                    await _db.SaveChangesAsync();

                    await WriteAuditAsync(parent.Id, actorUserId,
                        field:       "Status",
                        oldValue:    oldParentStatus,
                        newValue:    TaskStatus.Done.ToString(),
                        raciRole:    RaciRole.Accountable,
                        description: $"All workflow stages completed. Work package successfully closed by {actorRaciRole}.");
                }
            }
        }

        // ── Work Package Summary ────────────────────────────────────────────────
        /// <inheritdoc/>
        public async Task<WorkPackageSummary> GetWorkPackageSummaryAsync(int parentTaskId)
        {
            var parent = await _db.Tasks
                .FirstOrDefaultAsync(t => t.Id == parentTaskId)
                ?? throw new InvalidOperationException($"Parent task {parentTaskId} not found.");

            var subTasks = await _db.Tasks
                .Include(t => t.WorkflowStage)
                .Include(t => t.Assignee)
                .Include(t => t.History)
                .Where(t => t.ParentTaskId == parentTaskId && t.WorkflowStageId != null)
                .OrderBy(t => t.WorkflowStage!.Order)
                .ToListAsync();

            var stages = subTasks.Select(st =>
            {
                // Time-in-status: duration since last status change recorded in history
                var lastStatusChange = st.History
                    .Where(h => h.FieldChanged == "Status")
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefault();

                var timeInStatus = lastStatusChange != null
                    ? (DateTime.UtcNow - lastStatusChange.Timestamp).TotalHours
                    : 0;

                return new StageSummary
                {
                    StageOrder       = st.WorkflowStage?.Order ?? 0,
                    StageName        = st.WorkflowStage?.Name ?? string.Empty,
                    DefaultRoleTitle = st.WorkflowStage?.DefaultRoleTitle ?? string.Empty,
                    AssigneeName     = st.Assignee?.FullName ?? st.Assignee?.UserName,
                    OptimisticHours  = st.EstimatedOptimisticHours,
                    MostLikelyHours  = st.EstimatedMostLikelyHours,
                    PessimisticHours = st.EstimatedPessimisticHours,
                    PertHours        = st.PertEstimatedHours,
                    ActualHours      = st.ActualHours,
                    Status           = st.Status.ToString(),
                    TimeInStatusHours = timeInStatus
                };
            }).ToList();

            return new WorkPackageSummary
            {
                ParentTaskId           = parentTaskId,
                ParentTaskTitle        = parent.Title,
                TotalPertEstimatedHours = stages.Sum(s => s.PertHours ?? 0),
                TotalActualHours       = stages.Sum(s => s.ActualHours ?? 0),
                Stages                 = stages
            };
        }

        // ── Private Helpers ─────────────────────────────────────────────────────
        private async Task WriteAuditAsync(
            int taskId,
            string? actorUserId,
            string field,
            string? oldValue,
            string? newValue,
            RaciRole raciRole,
            string description)
        {
            _db.TaskHistories.Add(new TaskHistory
            {
                TaskItemId        = taskId,
                ChangedById       = actorUserId,
                FieldChanged      = field,
                OldValue          = oldValue,
                NewValue          = newValue,
                RaciRoleAtTime    = raciRole,
                ChangeDescription = description,
                Timestamp         = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }
}
