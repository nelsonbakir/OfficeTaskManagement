using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    /// <summary>
    /// Sprint-level capacity planning service.
    /// </summary>
    public interface ICapacityPlanningService
    {
        /// <summary>
        /// Sum of EstimatedHours across all tasks assigned to a sprint.
        /// </summary>
        Task<decimal> GetSprintDemandHoursAsync(int sprintId);

        /// <summary>
        /// Net team capacity for a sprint: sum of each assigned user's available hours
        /// between the sprint's StartDate and EndDate.
        /// </summary>
        Task<decimal> GetSprintCapacityHoursAsync(int sprintId);

        /// <summary>
        /// Returns demand vs. capacity summary for a sprint.
        /// </summary>
        Task<SprintCapacitySummary> GetSprintCapacityVsDemandAsync(int sprintId);

        /// <summary>
        /// Returns a per-user-per-day availability grid for a given month,
        /// used to render the heatmap. Value is available hours (0 = blocked/holiday).
        /// </summary>
        Task<HeatmapData> GetMonthlyHeatmapAsync(int year, int month);

        /// <summary>
        /// Returns capacity summaries for all sprints within a project.
        /// </summary>
        Task<IEnumerable<SprintCapacitySummary>> GetProjectSprintCapacitiesAsync(int projectId);
    }

    public class SprintCapacitySummary
    {
        public int SprintId { get; set; }
        public string SprintName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal PlannedCapacityHours { get; set; }
        public decimal DemandHours { get; set; }
        public decimal Delta => PlannedCapacityHours - DemandHours;
        public bool IsOverCommitted => DemandHours > PlannedCapacityHours;
    }

    public class HeatmapData
    {
        public int Year { get; set; }
        public int Month { get; set; }
        /// <summary>Users as rows.</summary>
        public List<HeatmapRow> Rows { get; set; } = new();
        /// <summary>Day numbers (1–28/29/30/31) as columns.</summary>
        public List<int> Days { get; set; } = new();
    }

    public class HeatmapRow
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        /// <summary>Key = day number, value = available hours (0 = unavailable).</summary>
        public Dictionary<int, decimal> DailyAvailability { get; set; } = new();
        /// <summary>Key = day number, value = allocation % (sum across all projects).</summary>
        public Dictionary<int, int> DailyAllocation { get; set; } = new();
    }
}
