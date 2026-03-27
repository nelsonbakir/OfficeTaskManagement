using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    /// <summary>
    /// Core resource capacity and conflict detection service.
    /// </summary>
    public interface IResourceService
    {
        /// <summary>
        /// Returns net available working hours for a user in a given date range,
        /// after subtracting availability blocks (leave, holidays, etc.).
        /// </summary>
        Task<decimal> GetUserAvailableHoursAsync(string userId, DateTime startDate, DateTime endDate);

        /// <summary>
        /// Returns the total allocation percentage for a user summed across all active projects.
        /// </summary>
        Task<int> GetUserTotalAllocationPercentAsync(string userId, DateTime? date = null);

        /// <summary>
        /// Returns true if the user's combined project allocations exceed 100% on any day
        /// within the given date range.
        /// </summary>
        Task<bool> IsUserOverAllocatedAsync(string userId, DateTime startDate, DateTime endDate);

        /// <summary>
        /// Returns a breakdown: list of (ProjectName, AllocationPct, StartDate, EndDate) for a user.
        /// </summary>
        Task<IEnumerable<AllocationSummaryItem>> GetUserAllocationSummaryAsync(string userId);

        /// <summary>
        /// Ensures a ResourceProfile exists for the user; creates a default one if missing.
        /// </summary>
        Task<ResourceProfile> GetOrCreateProfileAsync(string userId);

        /// <summary>
        /// Returns utilization percentage for a user in the given month (0–100+).
        /// Utilization = (allocated hours / available hours) × 100.
        /// </summary>
        Task<decimal> GetUserUtilizationPercentAsync(string userId, int year, int month);

        /// <summary>
        /// Returns utilization data for all active users for the given month,
        /// suitable for the heatmap / report card.
        /// </summary>
        Task<IEnumerable<UserUtilizationDto>> GetTeamUtilizationAsync(int year, int month);

        /// <summary>
        /// Returns the distinct set of assignee user IDs for tasks in the given sprint.
        /// Used by the Conflicts API to determine which users to check.
        /// </summary>
        Task<IEnumerable<string>> GetSprintAssigneeIdsAsync(int sprintId);

        /// <summary>
        /// Returns estimated labour cost per project (Manager-only).
        /// Cost = AllocationHours × HourlyRate per resource.
        /// </summary>
        Task<IEnumerable<ProjectCostReport>> GetProjectCostReportAsync();
    }

    public class AllocationSummaryItem
    {
        public int AllocationId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int AllocationPercentage { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? ProjectRole { get; set; }
    }

    public class ProjectCostReport
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public decimal EstimatedLaborCost { get; set; }
        public int ResourceCount { get; set; }
        public decimal TotalAllocatedHours { get; set; }
    }

    public class UserUtilizationDto
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Department { get; set; }
        
        /// <summary>Raw available hours after leave/holidays.</summary>
        public decimal AvailableHours { get; set; }
        
        /// <summary>Realistic productive capacity (usually 80% of AvailableHours per PMP standards).</summary>
        public decimal EffectiveCapacityHours => Math.Round(AvailableHours * 0.8m, 1);
        
        public decimal AllocatedHours { get; set; }
        
        /// <summary>Utilization against raw availability.</summary>
        public decimal UtilizationPercent { get; set; }
        
        /// <summary>Utilization against realistic productive capacity.</summary>
        public decimal EffectiveUtilizationPercent => EffectiveCapacityHours > 0 
            ? Math.Round((AllocatedHours / EffectiveCapacityHours) * 100, 1) 
            : 0;
            
        public decimal RemainingEffectiveHours => Math.Max(0, EffectiveCapacityHours - AllocatedHours);
        
        public bool IsOverAllocated => UtilizationPercent > 100;
        public bool IsAtRisk => EffectiveUtilizationPercent > 100 && !IsOverAllocated;
        public bool IsIdle => UtilizationPercent < 30;
    }
}
