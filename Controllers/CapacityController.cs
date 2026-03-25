using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Services;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class CapacityController : Controller
    {
        private readonly ICapacityPlanningService _capacityService;
        private readonly IResourceService _resourceService;

        public CapacityController(ICapacityPlanningService capacityService, IResourceService resourceService)
        {
            _capacityService = capacityService;
            _resourceService = resourceService;
        }

        // GET: Capacity
        public IActionResult Index()
        {
            // The dashboard view uses API calls to fetch heatmap and chart data
            return View();
        }

        // GET: Capacity/Sprint/5
        public async Task<IActionResult> Sprint(int id)
        {
            var summary = await _capacityService.GetSprintCapacityVsDemandAsync(id);
            if (summary == null) return NotFound();

            return View(summary);
        }
    }
}
