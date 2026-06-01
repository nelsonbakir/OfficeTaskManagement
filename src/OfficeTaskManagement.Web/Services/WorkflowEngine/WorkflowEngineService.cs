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
    /// enforces stage gate transitions, computes PERT estimates, produces
    /// work package summaries, and syncs parent status from children.
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

            // Remove any previously spawned stage sub-tasks to handle re-application
            var existing = await _db.Tasks
                .Where(t => t.ParentTaskId == parentTaskId && t.WorkflowStageId != null)
                .ToListAsync();
            _db.Tasks.RemoveRange(existing);

            // ── GAP 1: Transform parent into a Summary Task (Accountable-only) ──
            parent.IsWorkPackage  = true;
            parent.RaciRole       = RaciRole.Accountable;
            parent.Type           = TaskType.WorkPackage;
            parent.AssigneeId     = null;   // Delegate Responsible role to stage activities
            // Preserve AccountableUserId (the PM/Lead who created the work package)
            if (string.IsNullOrEmpty(parent.AccountableUserId))
                parent.AccountableUserId = parent.CreatedById;

            var stages = template.Stages.OrderBy(s => s.Order).ToList();
            TaskItem? previousSubTask = null;
            DateTime spawnTime = DateTime.UtcNow;

            foreach (var stage in stages)
            {
                // ── GAP 5: StartToStart — parallel stages activate with their predecessor ─
                var isStartToStart = stage.DependencyType == StageDependency.StartToStart
                                     && previousSubTask != null;

                // ── GAP 4: Calculate PlannedStartDate from LagHours ─────────────
                DateTime? plannedStart = null;
                if (stage.LagHours > 0)
                    plannedStart = spawnTime.AddHours((double)stage.LagHours);

                // Status: first stage or any StartToStart = ToDo; rest = New (locked)
                var initialStatus = (previousSubTask == null || isStartToStart)
                    ? TaskStatus.ToDo
                    : TaskStatus.New;

                var subTask = new TaskItem
                {
                    Title             = $"[{stage.Name}] {parent.Title}",
                    Description       = stage.DefinitionOfDone,
                    Status            = initialStatus,
                    Type              = TaskType.Activity,
                    Priority          = parent.Priority,
                    ProjectId         = parent.ProjectId,
                    SprintId          = parent.SprintId,
                    EpicId            = parent.EpicId,
                    FeatureId         = parent.FeatureId,
                    UserStoryId       = parent.UserStoryId,
                    ParentTaskId      = parentTaskId,
                    WorkflowStageId   = stage.Id,
                    RaciRole          = RaciRole.Responsible,
                    AccountableUserId = parent.AccountableUserId,
                    CreatedById       = parent.CreatedById,
                    CreatedAt         = spawnTime,
                    IsBacklog         = false,
                    IsWorkPackage     = false,
                    PlannedStartDate  = plannedStart
                };

                _db.Tasks.Add(subTask);
                previousSubTask = subTask;
            }

            await _db.SaveChangesAsync();

            // ── GAP 8: Bottom-up effort roll-up ─────────────────────────────────
            // Parent baseline = sum of all stage PERT or estimated hours
            // (At spawn time sub-tasks have no PERT yet; will re-sync when assignees estimate)
            // Set parent status to InProgress since first stage is now active
            parent.Status = TaskStatus.InProgress;
            await _db.SaveChangesAsync();

            // Write structured audit entry on parent
            await WriteAuditAsync(
                parentTaskId,
                actorUserId: parent.AccountableUserId,
                field:       "WorkflowTemplate",
                oldValue:    null,
                newValue:    template.Name,
                raciRole:    RaciRole.Accountable,
                description: $"Work Package created from template '{template.Name}'. {stages.Count} stage activities spawned. Parent transformed to Summary Task.");
        }

        // ── Stage Gate Transition ───────────────────────────────────────────────
        /// <inheritdoc/>
        public async Task TransitionStageAsync(int subTaskId, string actorUserId)
        {
            var current = await _db.Tasks
                .Include(t => t.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == subTaskId)
                ?? throw new InvalidOperationException($"Sub-task {subTaskId} not found.");

            // ── P3: Defence-in-depth IsPaused check ─────────────────────────────
            // StageGateService also checks this, but we enforce it here too to
            // prevent any bypass via direct engine calls (e.g. admin endpoints).
            if (current.IsPaused)
            {
                await WriteAuditAsync(subTaskId, actorUserId,
                    field: "TransitionAttempt",
                    oldValue: null,
                    newValue: "BLOCKED",
                    raciRole: RaciRole.Responsible,
                    description: $"Governance Block: Transition attempt on paused stage '{current.WorkflowStage?.Name}' was rejected. PauseReason: {current.PauseReason ?? "(none)"}");
                throw new InvalidOperationException(
                    $"This stage is governance-blocked (paused). " +
                    $"Reason: {(current.PauseReason ?? "No reason given")}. Contact the PM to unpause.");
            }

            // ── Enforce RACI-based stage gate — throws if criteria or permissions not met
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
            current.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await WriteAuditAsync(subTaskId, actorUserId,
                field:       "Status",
                oldValue:    oldStatus,
                newValue:    TaskStatus.Done.ToString(),
                raciRole:    actorRaciRole,
                description: $"Stage '{current.WorkflowStage?.Name}' completed. Gate passed by {actorRaciRole}.");

            if (nextSubTask != null)
            {
                var lag = nextSubTask.WorkflowStage?.LagHours ?? 0;

                // ── GAP 4: Record planned start even when we activate immediately ─
                if (lag > 0)
                    nextSubTask.PlannedStartDate = DateTime.UtcNow.AddHours((double)lag);

                // Activate the next stage (lag scheduling deferred to a future background job;
                // PlannedStartDate carries the intent for reporting/display)
                nextSubTask.Status = TaskStatus.ToDo;
                await _db.SaveChangesAsync();

                await WriteAuditAsync(nextSubTask.Id, actorUserId,
                    field:       "Status",
                    oldValue:    TaskStatus.New.ToString(),
                    newValue:    TaskStatus.ToDo.ToString(),
                    raciRole:    RaciRole.Accountable,
                    description: $"Stage '{nextSubTask.WorkflowStage?.Name}' activated after predecessor gate passed."
                        + (lag > 0 ? $" Planned start deferred by {lag}h." : ""));

                // ── GAP 10: Notify next-stage assignee ────────────────────────────
                if (!string.IsNullOrEmpty(nextSubTask.AssigneeId))
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId  = nextSubTask.AssigneeId,
                        Title   = "Your Stage Is Ready",
                        Message = $"Stage '{nextSubTask.WorkflowStage?.Name}' on '{nextSubTask.Title.Replace($"[{nextSubTask.WorkflowStage?.Name}] ", "")}' is now active and waiting for you.",
                        Link    = $"/TaskItems/Edit/{nextSubTask.Id}",
                        Type    = "WorkflowActivation"
                    });
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                // All stages done — close the parent Work Package
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
                        description: $"All workflow stages completed. Work package closed by {actorRaciRole}.");
                    return; // Skip SyncParentStatus — we just set it to Done explicitly
                }
            }

            // ── GAP 2: Roll up parent status from children ────────────────────────
            if (current.ParentTaskId.HasValue)
                await SyncParentStatusAsync(current.ParentTaskId.Value, actorUserId);
        }

        // ── GAP 2: Parent Status Roll-up ────────────────────────────────────────
        /// <inheritdoc/>
        public async Task SyncParentStatusAsync(int parentId, string? actorUserId = null)
        {
            var parent = await _db.Tasks.FindAsync(parentId);
            if (parent == null || !parent.IsWorkPackage) return;

            var children = await _db.Tasks
                .Where(t => t.ParentTaskId == parentId && t.WorkflowStageId != null)
                .ToListAsync();

            if (!children.Any()) return;

            // Bottom-up status: parent reflects the most-progressed active child
            var allDone       = children.All(c => c.Status == TaskStatus.Done);
            var anyTested     = children.Any(c => c.Status == TaskStatus.Tested);
            var anyReviewed   = children.Any(c => c.Status == TaskStatus.Reviewed);
            var anyCommitted  = children.Any(c => c.Status == TaskStatus.Committed);
            var anyInProgress = children.Any(c => c.Status == TaskStatus.InProgress);
            var anyToDo       = children.Any(c => c.Status == TaskStatus.ToDo);

            var derivedStatus = allDone       ? TaskStatus.Done
                              : anyTested     ? TaskStatus.Tested
                              : anyReviewed   ? TaskStatus.Reviewed
                              : anyCommitted  ? TaskStatus.Committed
                              : anyInProgress ? TaskStatus.InProgress
                              : anyToDo       ? TaskStatus.ToDo
                              : parent.Status; // No change if all children are still New

            // ── GAP 8: Bottom-up effort roll-up ─────────────────────────────────
            var totalPert    = children.Sum(c => c.PertEstimatedHours ?? c.EstimatedHours);
            var totalActual  = children.Sum(c => c.ActualHours ?? 0);

            var statusChanged = parent.Status != derivedStatus;
            parent.Status         = derivedStatus;
            parent.EstimatedHours = totalPert;
            parent.ActualHours    = totalActual > 0 ? totalActual : null;

            await _db.SaveChangesAsync();

            if (statusChanged && actorUserId != null)
            {
                await WriteAuditAsync(parentId, actorUserId,
                    field:       "Status",
                    oldValue:    null,
                    newValue:    derivedStatus.ToString(),
                    raciRole:    RaciRole.Accountable,
                    description: $"Work Package status rolled up from stage activities → {derivedStatus}.");
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
                var lastStatusChange = st.History
                    .Where(h => h.FieldChanged == "Status")
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefault();

                var timeInStatus = lastStatusChange != null
                    ? (DateTime.UtcNow - lastStatusChange.Timestamp).TotalHours
                    : 0;

                return new StageSummary
                {
                    StageOrder        = st.WorkflowStage?.Order ?? 0,
                    StageName         = st.WorkflowStage?.Name ?? string.Empty,
                    DefaultRoleTitle  = st.WorkflowStage?.DefaultRoleTitle ?? string.Empty,
                    AssigneeName      = st.Assignee?.FullName ?? st.Assignee?.UserName,
                    OptimisticHours   = st.EstimatedOptimisticHours,
                    MostLikelyHours   = st.EstimatedMostLikelyHours,
                    PessimisticHours  = st.EstimatedPessimisticHours,
                    PertHours         = st.PertEstimatedHours,
                    ActualHours       = st.ActualHours,
                    Status            = st.Status.ToString(),
                    TimeInStatusHours = timeInStatus
                };
            }).ToList();

            return new WorkPackageSummary
            {
                ParentTaskId            = parentTaskId,
                ParentTaskTitle         = parent.Title,
                TotalPertEstimatedHours = stages.Sum(s => s.PertHours ?? 0),
                TotalActualHours        = stages.Sum(s => s.ActualHours ?? 0),
                Stages                  = stages
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
