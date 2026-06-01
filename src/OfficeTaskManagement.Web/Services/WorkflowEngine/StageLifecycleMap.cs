using System.Collections.Generic;
using OfficeTaskManagement.Models.Enums;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Single source of truth for how a StageGateType maps to a TaskStatus
    /// and a full activity lifecycle (Kanban steps) for that gate type.
    ///
    /// This class is the bridge between the generic Workflow Template system
    /// and the Kanban board. The template defines GateType; GateType defines
    /// which Kanban columns a sub-task moves through.
    ///
    /// Design rules:
    ///   CommittedOnly          → terminal = Committed  (light delivery: docs, handover)
    ///   CommittedWithHours     → terminal = Committed  (build/dev: evidenced by hours logged)
    ///   CommittedWithPeerReview→ terminal = Reviewed   (any peer-review: code, design, audit)
    ///   TestedWithAllCasesPassed→ terminal = Tested    (test execution: QA, UAT, regression)
    ///   None                   → no terminal state     (planning, kickoff — passes freely)
    /// </summary>
    public static class StageLifecycleMap
    {
        /// <summary>
        /// Returns the TaskStatus the assignee must set before the gate can be passed.
        /// Returns null when the gate type has no terminal status requirement (None).
        /// </summary>
        public static TaskStatus? TerminalStatus(StageGateType gate) => gate switch
        {
            StageGateType.None                     => null,
            StageGateType.CommittedOnly            => TaskStatus.Committed,
            StageGateType.CommittedWithHours       => TaskStatus.Committed,
            StageGateType.CommittedWithPeerReview  => TaskStatus.Reviewed,
            StageGateType.TestedWithAllCasesPassed => TaskStatus.Tested,
            _                                      => null
        };

        /// <summary>
        /// Returns the ordered list of TaskStatus values a sub-task moves through
        /// on the Kanban board for a given gate type.
        /// This is the "lifecycle" of that activity — only these columns are relevant.
        /// </summary>
        public static List<TaskStatus> LifecycleFor(StageGateType gate)
        {
            var steps = new List<TaskStatus> { TaskStatus.ToDo, TaskStatus.InProgress };

            var terminal = TerminalStatus(gate);
            if (terminal.HasValue)
                steps.Add(terminal.Value);

            steps.Add(TaskStatus.Done);
            return steps;
        }

        /// <summary>
        /// Returns a user-friendly description of what the assignee must do
        /// to satisfy the gate and advance to the next stage.
        /// Used in the Edit form and Definition-of-Done panel.
        /// </summary>
        public static string GateInstruction(StageGateType gate) => gate switch
        {
            StageGateType.None                     => "No specific gate — complete your work and mark Done.",
            StageGateType.CommittedOnly            => "Set status to 'Committed' when this stage is delivered.",
            StageGateType.CommittedWithHours       => "Log actual hours and set status to 'Committed'.",
            StageGateType.CommittedWithPeerReview  => "Get peer review, add a comment, then set status to 'Reviewed'.",
            StageGateType.TestedWithAllCasesPassed => "Run all test cases, mark them Passed, then set status to 'Tested'.",
            _                                      => "Complete the work and advance to the next stage."
        };
    }
}
