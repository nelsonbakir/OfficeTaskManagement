using System.Collections.Generic;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// DTO returned by IWorkflowEngineService.GetWorkPackageSummaryAsync.
    /// Provides PM-level visibility into the full effort breakdown of a work package.
    /// </summary>
    public class WorkPackageSummary
    {
        public int ParentTaskId { get; set; }
        public string ParentTaskTitle { get; set; } = string.Empty;

        /// <summary>Sum of all child stage PERT estimated hours (the PM baseline).</summary>
        public decimal TotalPertEstimatedHours { get; set; }

        /// <summary>Sum of all child stage actual hours logged by Responsible parties.</summary>
        public decimal TotalActualHours { get; set; }

        /// <summary>Variance = TotalActualHours - TotalPertEstimatedHours. Positive = over budget.</summary>
        public decimal EffortVarianceHours => TotalActualHours - TotalPertEstimatedHours;

        /// <summary>Effort variance expressed as a percentage of the baseline.</summary>
        public decimal EffortVariancePercent =>
            TotalPertEstimatedHours > 0
                ? (EffortVarianceHours / TotalPertEstimatedHours) * 100
                : 0;

        /// <summary>Per-stage breakdown for detailed reporting.</summary>
        public List<StageSummary> Stages { get; set; } = new();
    }

    /// <summary>Per-stage row within a WorkPackageSummary.</summary>
    public class StageSummary
    {
        public int StageOrder { get; set; }
        public string StageName { get; set; } = string.Empty;
        public string DefaultRoleTitle { get; set; } = string.Empty;
        public string? AssigneeName { get; set; }

        public decimal? OptimisticHours { get; set; }
        public decimal? MostLikelyHours { get; set; }
        public decimal? PessimisticHours { get; set; }
        public decimal? PertHours { get; set; }
        public decimal? ActualHours { get; set; }

        public decimal VarianceHours => (ActualHours ?? 0) - (PertHours ?? 0);

        /// <summary>Current status of this stage's sub-task.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>How long (hours) this stage has been in its current status.</summary>
        public double TimeInStatusHours { get; set; }
    }
}
