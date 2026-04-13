namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Defines the Definition-of-Done (DoD) criteria pattern that StageGateService
    /// enforces before a workflow stage can be transitioned to the next.
    ///
    /// Values describe WHAT must be true, not WHO performs the work.
    /// This makes them reusable across any custom pipeline template:
    ///   e.g. "Security Audit" → CommittedWithPeerReview
    ///        "UAT"            → TestedWithAllCasesPassed
    ///        "Deployment"     → CommittedOnly
    /// </summary>
    public enum StageGateType
    {
        /// <summary>
        /// No gate — stage transitions freely.
        /// Use for: Planning, Kickoff, Documentation, Handover stages where
        /// no programmatic DoD can be enforced.
        /// </summary>
        None = 0,

        /// <summary>
        /// Status must be <see cref="TaskStatus.Committed"/>.
        /// Use for: Light delivery stages where completion intent is sufficient
        /// (e.g. "Deployed to Staging", "Docs Written", "Design Signed Off").
        /// </summary>
        CommittedOnly = 1,

        /// <summary>
        /// Status must be Committed AND ActualHours must be &gt; 0.
        /// Use for: Any implementation or build stage that requires evidence of work
        /// (e.g. "Development", "Infrastructure Setup", "Data Migration").
        /// </summary>
        CommittedWithHours = 2,

        /// <summary>
        /// Status must be Committed AND at least one comment must exist on the sub-task.
        /// Use for: Any peer validation stage that requires a written review record
        /// (e.g. "Code Review", "Design Review", "Security Audit", "Change Approval").
        /// </summary>
        CommittedWithPeerReview = 3,

        /// <summary>
        /// Status must be <see cref="TaskStatus.Tested"/> AND all TestCases linked to
        /// the parent's UserStory must be marked as Passed.
        /// Use for: Any test execution stage that requires formal test evidence
        /// (e.g. "QA Testing", "User Acceptance Testing", "Regression Verification").
        /// </summary>
        TestedWithAllCasesPassed = 4
    }
}
