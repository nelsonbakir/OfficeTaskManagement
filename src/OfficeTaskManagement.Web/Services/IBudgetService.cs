using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Services
{
    /// <summary>
    /// Budget management service — covers PMBOK Cost Management processes:
    /// Plan Cost Management (7.1), Determine Budget (7.3), Control Costs (7.4).
    /// </summary>
    public interface IBudgetService
    {
        // ── Budget Baseline ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets or updates the budget baseline for a project.
        /// Idempotent — calling multiple times updates the existing budget fields.
        /// </summary>
        Task SetProjectBudgetAsync(
            int projectId,
            BudgetMode mode,
            decimal? approvedBudget,
            decimal? contingencyReserve,
            string? setById);

        /// <summary>
        /// Returns the consolidated budget summary for a project, combining
        /// labour costs (from resource allocations and task estimates) and
        /// non-labour other-cost line items.
        /// </summary>
        Task<ProjectBudgetSummary> GetBudgetSummaryAsync(int projectId);

        // ── Cost Forecasting ────────────────────────────────────────────────────

        /// <summary>
        /// Computes the bottom-up labour cost from PERT-estimated task hours
        /// multiplied by the effective hourly rate of the assigned resource.
        /// Returns 0 when no tasks or rates are available.
        /// </summary>
        Task<decimal> GetLaborCostEstimateAsync(int projectId);

        /// <summary>
        /// Computes the labour cost from high-level resource allocations
        /// (AllocationPercentage × DailyCapacityHours × HourlyRate × working days).
        /// This is the Planned Value (PV) figure per PMBOK EVM.
        /// </summary>
        Task<decimal> GetLaborCostAllocatedAsync(int projectId);

        /// <summary>
        /// Returns a live-computed derived budget total:
        ///   Estimated Labour + Other Costs Estimated.
        /// Note: never auto-saved — remains a live forecast.
        /// </summary>
        Task<DerivedBudgetForecast> GetDerivedBudgetForecastAsync(int projectId);

        // ── Advisory Intelligence ───────────────────────────────────────────────

        /// <summary>
        /// Returns a budget advisory DTO used by the resource-allocation form.
        /// Computes what impact the proposed additional labour cost would have
        /// on the project's budget utilisation.
        /// </summary>
        /// <param name="projectId">Project being allocated to.</param>
        /// <param name="proposedAdditionalCost">
        ///   Estimated incremental labour cost of the new allocation (BDT).
        ///   Caller computes this as: AllocationHours × ResourceHourlyRate.
        /// </param>
        Task<BudgetAdvisory> GetBudgetAdvisoryAsync(int projectId, decimal proposedAdditionalCost);

        // ── Other Costs CRUD ────────────────────────────────────────────────────

        /// <summary>Returns all non-labour cost line items for a project, newest first.</summary>
        Task<IEnumerable<ProjectOtherCost>> GetOtherCostsAsync(int projectId);

        /// <summary>Adds a new non-labour cost line item to the project.</summary>
        Task<ProjectOtherCost> AddOtherCostAsync(OtherCostUpsertDto dto, string? createdById);

        /// <summary>Updates an existing non-labour cost line item.</summary>
        Task<ProjectOtherCost> UpdateOtherCostAsync(int costId, OtherCostUpsertDto dto);

        /// <summary>Deletes a non-labour cost line item by ID.</summary>
        Task DeleteOtherCostAsync(int costId);
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consolidated view of a project's budget: approved baseline, contingency,
    /// labour costs, non-labour costs, variance, and health status.
    /// </summary>
    public class ProjectBudgetSummary
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public BudgetMode BudgetMode { get; set; }

        // ── Approved Baseline ────────────────────────────────────────────────
        public decimal? ApprovedBudget { get; set; }
        public decimal? ContingencyReserve { get; set; }

        /// <summary>ApprovedBudget + ContingencyReserve = total budget ceiling.</summary>
        public decimal? TotalBudgetCeiling =>
            ApprovedBudget.HasValue ? ApprovedBudget.Value + (ContingencyReserve ?? 0) : null;

        public DateTime? BudgetSetAt { get; set; }
        public string? BudgetSetByName { get; set; }

        // ── Labour Cost Components ───────────────────────────────────────────
        /// <summary>Strategic: Planned Value from high-level resource allocations (PV).</summary>
        public decimal LaborCostAllocated { get; set; }

        /// <summary>Bottom-up: Estimated labour cost from PERT task hours × hourly rates (EAC).</summary>
        public decimal LaborCostEstimated { get; set; }

        // ── Non-Labour Cost Components ───────────────────────────────────────
        public decimal OtherCostEstimated { get; set; }
        public decimal OtherCostActual { get; set; }
        public int OtherCostLineItemCount { get; set; }

        // ── Totals ───────────────────────────────────────────────────────────
        /// <summary>Total estimated cost (bottom-up labour + other cost estimates).</summary>
        public decimal TotalEstimatedCost => LaborCostEstimated + OtherCostEstimated;

        /// <summary>Total actual cost so far (actual labour not tracked in this phase + actual other costs).</summary>
        public decimal TotalActualOtherCost => OtherCostActual;

        // ── EVM-style Indicators ─────────────────────────────────────────────
        /// <summary>
        /// Budget Variance: how much headroom (positive) or overrun (negative) exists.
        /// Null when no approved budget is set.
        /// </summary>
        public decimal? BudgetVariance =>
            ApprovedBudget.HasValue ? ApprovedBudget.Value - TotalEstimatedCost : null;

        /// <summary>
        /// Percentage of the approved budget consumed by the total estimated cost.
        /// Null when no approved budget is set.
        /// </summary>
        public decimal? BudgetUtilizationPercent =>
            ApprovedBudget.HasValue && ApprovedBudget > 0
                ? Math.Round((TotalEstimatedCost / ApprovedBudget.Value) * 100, 1)
                : null;

        // ── Health Status ────────────────────────────────────────────────────
        public BudgetHealthStatus HealthStatus { get; set; } = BudgetHealthStatus.NotSet;
        public string HealthReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Live bottom-up derived budget forecast.
    /// Never persisted — always computed on demand.
    /// </summary>
    public class DerivedBudgetForecast
    {
        public int ProjectId { get; set; }
        public decimal LaborEstimate { get; set; }
        public decimal OtherCostEstimate { get; set; }
        public decimal TotalForecast => LaborEstimate + OtherCostEstimate;
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Advisory returned to the resource allocation form before saving.</summary>
    public class BudgetAdvisory
    {
        public bool HasBudget { get; set; }
        public decimal? ApprovedBudget { get; set; }
        public decimal CurrentTotalEstimate { get; set; }
        public decimal ProposedAdditionalCost { get; set; }
        public decimal ProjectedTotalCost => CurrentTotalEstimate + ProposedAdditionalCost;
        public decimal? ProjectedUtilizationPercent =>
            ApprovedBudget.HasValue && ApprovedBudget > 0
                ? Math.Round((ProjectedTotalCost / ApprovedBudget.Value) * 100, 1)
                : null;
        public BudgetAdvisoryLevel Level { get; set; } = BudgetAdvisoryLevel.None;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>DTO for create/update of a non-labour cost line item.</summary>
    public class OtherCostUpsertDto
    {
        public int ProjectId { get; set; }
        public string Description { get; set; } = string.Empty;
        public OtherCostCategory Category { get; set; } = OtherCostCategory.Miscellaneous;
        public CostFrequency Frequency { get; set; } = CostFrequency.OneTime;
        public decimal EstimatedAmount { get; set; }
        public decimal? ActualAmount { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? ActualDate { get; set; }
        public bool IsContingency { get; set; } = false;
        public string? Notes { get; set; }
    }

    // ── Enums used by BudgetService ──────────────────────────────────────────────

    public enum BudgetHealthStatus
    {
        /// <summary>No approved budget — health cannot be determined.</summary>
        NotSet = 0,
        /// <summary>Estimated cost is below 80% of approved budget.</summary>
        OnTrack = 1,
        /// <summary>Estimated cost is between 80% and 100% of approved budget.</summary>
        AtRisk = 2,
        /// <summary>Estimated cost exceeds the approved budget.</summary>
        Exceeded = 3
    }

    public enum BudgetAdvisoryLevel
    {
        /// <summary>No budget defined — no advisory shown.</summary>
        None = 0,
        /// <summary>Below 80% utilisation after proposed allocation.</summary>
        Info = 1,
        /// <summary>Between 80% and 100% utilisation after proposed allocation.</summary>
        Warning = 2,
        /// <summary>Exceeds 100% utilisation after proposed allocation.</summary>
        Critical = 3
    }
}
