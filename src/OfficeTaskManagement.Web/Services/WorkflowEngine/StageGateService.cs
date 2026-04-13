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
    /// stage transition is permitted. Each gate is checked per stage role:
    ///   - Developer → Review Gate: Status=Committed + ActualHours logged
    ///   - Review → QA Gate:       Status=Committed + at least 1 reviewer comment
    ///   - QA → Done Gate:         Status=Tested + all linked TestCases passed
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

            // ── RACI Enforcement ────────────────────────────────────────────────
            // 1. Verify if Accountable sign-off is required and provided by an Accountable user
            if (stage.RequiresAccountableSignoff)
            {
                if (actorUserId != subTask.AccountableUserId)
                {
                    throw new InvalidOperationException(
                        $"Governance Gate: Stage '{stage.Name}' requires sign-off from the Accountable party. " +
                        "Only the project Lead or PM can transition this gate.");
                }
            }
            // 2. Otherwise, ensure the actor is the assigned Responsible party
            else if (actorUserId != subTask.AssigneeId)
            {
                throw new InvalidOperationException(
                    "RACI Violation: You must be the assigned 'Responsible' user to transition this stage. " +
                    "Assign the task to yourself or contact the owner.");
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
                    if (subTask.Status != TaskStatus.Committed)
                        throw new InvalidOperationException(
                            $"{stage.Name} Gate: Task must be in 'Committed' status (Review Approved).");

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
