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
        /// Throws <see cref="InvalidOperationException"/> if the gate is not satisfied.
        /// </summary>
        public async Task EnforceGateAsync(TaskItem subTask)
        {
            var stage = subTask.WorkflowStage
                ?? await _db.WorkflowStages.FindAsync(subTask.WorkflowStageId);

            if (stage == null)
            {
                // No stage context — standalone task, no gate to enforce
                return;
            }

            var roleTitle = stage.DefaultRoleTitle.ToLowerInvariant();

            // ── Gate 1: Developer Stage ─────────────────────────────────────────
            if (roleTitle.Contains("developer") || roleTitle.Contains("development"))
            {
                if (subTask.Status != TaskStatus.Committed)
                    throw new InvalidOperationException(
                        "Development Gate: Task must be in 'Committed' status before transitioning to Code Review. " +
                        "Set Status = Committed when your implementation is ready.");

                if ((subTask.ActualHours ?? 0) <= 0)
                    throw new InvalidOperationException(
                        "Development Gate: Actual hours must be logged before the gate can pass. " +
                        "Please record the time spent on implementation.");
            }

            // ── Gate 2: Code Review Stage ───────────────────────────────────────
            else if (roleTitle.Contains("review"))
            {
                if (subTask.Status != TaskStatus.Committed)
                    throw new InvalidOperationException(
                        "Code Review Gate: Task must be in 'Committed' status before transitioning to QA. " +
                        "Set Status = Committed when review is approved.");

                var hasReviewerComment = await _db.TaskComments
                    .AnyAsync(c => c.TaskId == subTask.Id);

                if (!hasReviewerComment)
                    throw new InvalidOperationException(
                        "Code Review Gate: At least one review comment must be recorded before the gate can pass. " +
                        "Add your review findings or approval note as a comment.");
            }

            // ── Gate 3: QA / Testing Stage ──────────────────────────────────────
            else if (roleTitle.Contains("qa") || roleTitle.Contains("test"))
            {
                if (subTask.Status != TaskStatus.Tested)
                    throw new InvalidOperationException(
                        "QA Gate: Task must be in 'Tested' status before closing the work package. " +
                        "Set Status = Tested when all test cases have been executed.");

                // Check linked UserStory test cases if the parent has a UserStoryId
                if (subTask.UserStoryId.HasValue)
                {
                    var allPassed = await _db.TestCases
                        .Where(tc => tc.UserStoryId == subTask.UserStoryId)
                        .AllAsync(tc => tc.IsPassed);

                    if (!allPassed)
                        throw new InvalidOperationException(
                            "QA Gate: Not all test cases are marked as passed. " +
                            "All linked test cases must pass before closing the QA stage.");
                }
            }
        }
    }
}
