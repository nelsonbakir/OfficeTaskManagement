using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Services;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ResourceDataController : ControllerBase
    {
        private readonly IResourceService _resourceService;
        private readonly ICapacityPlanningService _capacityService;

        public ResourceDataController(IResourceService resourceService, ICapacityPlanningService capacityService)
        {
            _resourceService = resourceService;
            _capacityService = capacityService;
        }

        [HttpGet("utilization")]
        public async Task<IActionResult> GetTeamUtilization(int? month, int? year)
        {
            var targetDate = new DateTime(year ?? DateTime.UtcNow.Year, month ?? DateTime.UtcNow.Month, 1);
            var data = await _resourceService.GetTeamUtilizationAsync(targetDate.Year, targetDate.Month);
            return Ok(data);
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetMonthlyHeatmap(int? month, int? year)
        {
            var targetDate = new DateTime(year ?? DateTime.UtcNow.Year, month ?? DateTime.UtcNow.Month, 1);
            var data = await _capacityService.GetMonthlyHeatmapAsync(targetDate.Year, targetDate.Month);
            return Ok(data);
        }

        [HttpGet("availability")]
        public async Task<IActionResult> CheckUserAvailability(string userId, DateTime startDate, DateTime endDate)
        {
            var hours = await _resourceService.GetUserAvailableHoursAsync(userId, startDate, endDate);
            var pct = await _resourceService.GetUserTotalAllocationPercentAsync(userId, startDate);
            var overAllocated = await _resourceService.IsUserOverAllocatedAsync(userId, startDate, endDate);

            return Ok(new 
            {
                availableHours = hours,
                currentAllocationPercent = pct,
                isOverAllocated = overAllocated
            });
        }

        /// <summary>
        /// Returns over-allocation conflicts for all resources in a given sprint.
        /// GET /api/ResourceData/conflicts?sprintId=5
        /// </summary>
        [HttpGet("conflicts")]
        public async Task<IActionResult> GetSprintConflicts(int sprintId)
        {
            var sprint = await _capacityService.GetSprintCapacityVsDemandAsync(sprintId);
            if (sprint == null) return NotFound();

            var assigneeIds = await _resourceService.GetSprintAssigneeIdsAsync(sprintId);

            var conflicts = new List<object>();
            foreach (var userId in assigneeIds)
            {
                var overAllocated = await _resourceService.IsUserOverAllocatedAsync(
                    userId, sprint.StartDate, sprint.EndDate);

                if (overAllocated)
                {
                    var pct = await _resourceService.GetUserTotalAllocationPercentAsync(userId, sprint.StartDate);
                    conflicts.Add(new
                    {
                        userId,
                        sprintId,
                        allocationPercent = pct,
                        isOverAllocated = true
                    });
                }
            }

            return Ok(new
            {
                sprintId,
                sprintName = sprint.SprintName,
                conflictCount = conflicts.Count,
                conflicts
            });
        }
        /// <summary>
        /// Returns estimated labour cost per project (Manager-only).
        /// GET /api/ResourceData/cost-report
        /// </summary>
        [HttpGet("cost-report")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> GetCostReport()
        {
            var report = await _resourceService.GetProjectCostReportAsync();
            return Ok(report);
        }
    }
}
