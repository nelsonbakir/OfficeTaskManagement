namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Describes how a project's budget was established.
    /// Drives UI behaviour and budget-advisory logic in resource allocation.
    /// </summary>
    public enum BudgetMode
    {
        /// <summary>No budget has been defined yet — advisory warnings are suppressed.</summary>
        NotSet = 0,

        /// <summary>
        /// Budget was set up-front (top-down) before work items are detailed.
        /// The system uses <see cref="Project.ApprovedBudget"/> as the cost ceiling
        /// for advisory warnings during resource allocation.
        /// </summary>
        PreApproved = 1,

        /// <summary>
        /// Budget is derived bottom-up from task PERT estimated hours × resource hourly
        /// rates, plus other-cost line items.  <see cref="Project.ApprovedBudget"/> is
        /// null; the system computes a live forecast only — it is never auto-saved.
        /// </summary>
        DerivedFromWork = 2,

        /// <summary>
        /// A pre-approved ceiling exists AND the PM also tracks the bottom-up
        /// derived forecast for variance analysis (classic EVM workflow).
        /// </summary>
        Hybrid = 3
    }
}
