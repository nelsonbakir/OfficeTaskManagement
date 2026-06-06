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
    /// Enforces the Definition of Done (DoD) gate criteria before a workflow
    /// stage transition is permitted. Gate → required terminal status:
    ///   - CommittedWithHours:      Status = Committed  + ActualHours logged
    ///   - CommittedWithPeerReview: Status = Reviewed   + at least 1 reviewer comment
    ///   - TestedWithAllCasesPassed:Status = Tested     + all linked TestCases passed
    ///   - CommittedOnly:           Status = Committed
    ///   - None:                    no status check
    /// See StageLifecycleMap for the authoritative gate → status mapping.
    /// Throws InvalidOperationException with a user-facing message on failure.
    /// </summary>
    public class StageGateService
    {
        private readonly ApplicationDbContext _db;

        public StageGateService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Validates the DoD for the given sub-task's stage.
        /// Throws <see cref="InvalidOperationException"/> if the gate is not satisfied
        /// or if the actor is not authorized per RACI roles.
        /// </summary>
        public async Task EnforceGateAsync(TaskItem subTask, string actorUserId)
        {
            var stage = subTask.WorkflowStage
                ?? await _db.WorkflowStages.FindAsync(subTask.WorkflowStageId);

            if (stage == null)
            {
                // No stage context — standalone task, no gate to enforce
                return;
            }

            // ── P3: IsPaused Guard ───────────────────────────────────────────────
            // A paused task is governance-blocked: no gate transitions until unpaused by the PM.
            if (subTask.IsPaused)
            {
                var pausedByName = subTask.PausedById != null
                    ? (await _db.Users.FindAsync(subTask.PausedById))?.FullName ?? "PM"
                    : "PM";
                var reason = !string.IsNullOrWhiteSpace(subTask.PauseReason)
                    ? $": \"{subTask.PauseReason}\""
                    : string.Empty;

                throw new InvalidOperationException(
                    $"Governance Block: Stage '{stage.Name}' is paused by {pausedByName}{reason}. " +
                    "The task must be unpaused before this gate can be transitioned.");
            }

            // Also check the parent Work Package for a pause flag — if the WP is paused, all its stages are blocked
            if (subTask.ParentTaskId.HasValue)
            {
                var parent = await _db.Tasks.FindAsync(subTask.ParentTaskId.Value);
                if (parent is { IsPaused: true })
                {
                    var reason = !string.IsNullOrWhiteSpace(parent.PauseReason)
                        ? $": \"{parent.PauseReason}\""
                        : string.Empty;
                    throw new InvalidOperationException(
                        $"Governance Block: The parent Work Package is paused{reason}. " +
                        "All stage gates are blocked until the Work Package is unpaused.");
                }
            }
            // ────────────────────────────────────────────────────────────────────

            // ── RACI Enforcement ────────────────────────────────────────────────
            // Fetch actor's roles to check hierarchy
            var actorRoleIds = await _db.UserRoles.Where(ur => ur.UserId == actorUserId).Select(ur => ur.RoleId).ToListAsync();
            var actorRoles = await _db.Roles.Where(r => actorRoleIds.Contains(r.Id)).ToListAsync();
            var minActorLevel = actorRoles.Any() ? actorRoles.Min(r => r.HierarchyLevel) : int.MaxValue;

            // Higher authority roles (Super Admin = 0, Admin = 1, PM = 2, Project Lead = 3)
            bool isHigherAuthority = minActorLevel <= 3;

            // 1. Verify if Accountable sign-off is required and provided by an Accountable user (or higher authority)
            if (stage.RequiresAccountableSignoff)
            {
                if (actorUserId != subTask.AccountableUserId && !isHigherAuthority)
                {
                    throw new InvalidOperationException(
                        $"Governance Gate: Stage '{stage.Name}' requires sign-off from the Accountable party. " +
                        "Only the project Lead or PM can transition this gate.");
                }
            }
            // 2. Otherwise, ensure the actor is the assigned Responsible party OR holds a higher authority role
            else if (actorUserId != subTask.AssigneeId && !isHigherAuthority)
            {
                throw new InvalidOperationException(
                    "RACI Violation: You must be the assigned 'Responsible' user to transition this stage. " +
                    "Assign the task to yourself or contact the owner.");
            }

            // 3. Enforce the required dynamic role restriction if configured
            if (stage.RoleId != null)
            {
                var stageRole = await _db.Roles.FindAsync(stage.RoleId);
                if (stageRole != null)
                {
                    bool hasAuthorizedRole = minActorLevel <= stageRole.HierarchyLevel;
                    if (!hasAuthorizedRole)
                    {
                        throw new InvalidOperationException(
                            $"RACI Role Restriction: You must hold the '{stageRole.Name}' role (or a higher authority role) to transition this stage.");
                    }
                }
            }

            // ── Gate Enforcement by Type ────────────────────────────────────────
            switch (stage.GateType)
            {
                case StageGateType.None:
                    // No programmatic gate to enforce
                    break;

                case StageGateType.CommittedOnly:
                    if (subTask.Status != TaskStatus.Committed)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Task must be in 'Committed' status to pass. " +
                            "Set Status = Committed when the work package is delivered.");
                    break;

                case StageGateType.CommittedWithHours:
                    if (subTask.Status != TaskStatus.Committed)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Task must be in 'Committed' status to pass.");

                    if ((subTask.ActualHours ?? 0) <= 0)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Actual hours must be logged as evidence of work performed.");
                    break;

                case StageGateType.CommittedWithPeerReview:
                    // Review/audit/approval gates require Status = Reviewed (not Committed).
                    // This distinguishes a reviewed task from a committed-but-not-reviewed one
                    // and drives the correct "Reviewed" Kanban column placement.
                    if (subTask.Status != TaskStatus.Reviewed)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Set status to 'Reviewed' to confirm the review is complete, " +
                            "then ensure at least one review comment is recorded.");

                    var hasComment = await _db.TaskComments.AnyAsync(c => c.TaskId == subTask.Id);
                    if (!hasComment)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: At least one reviewer comment or approval note must be recorded.");
                    break;

                case StageGateType.TestedWithAllCasesPassed:
                    if (subTask.Status != TaskStatus.Tested)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Task must be in 'Tested' status.");

                    if (subTask.UserStoryId.HasValue)
                    {
                        var allPassed = await _db.TestCases
                            .Where(tc => tc.UserStoryId == subTask.UserStoryId)
                            .AllAsync(tc => tc.IsPassed);

                        if (!allPassed)
                            throw new InvalidOperationException(
                                $"{stage.Name} Gate: All linked test cases must be marked as 'Passed' before closing.");
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(stage.GateType), "Unsupported StageGateType.");
            }
        }
    }
}
