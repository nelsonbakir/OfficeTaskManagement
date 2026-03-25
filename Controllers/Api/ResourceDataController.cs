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
    }
}
