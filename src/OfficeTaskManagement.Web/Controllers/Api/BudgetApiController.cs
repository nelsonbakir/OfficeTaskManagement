using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Authorization;

namespace OfficeTaskManagement.Controllers.Api
{
    /// <summary>
    /// REST API for project budget management and cost advisory.
    /// All endpoints are Bearer-authenticated; budget.manage permission
    /// required for mutations; budget.view for reads.
    /// </summary>
    [ApiController]
    [Route("api/budget")]
    [Authorize]
    public class BudgetApiController : ControllerBase
    {
        private readonly IBudgetService _budgetService;
        private readonly IPermissionService _permSvc;
        private readonly UserManager<User> _userManager;

        public BudgetApiController(
            IBudgetService budgetService,
            IPermissionService permSvc,
            UserManager<User> userManager)
        {
            _budgetService = budgetService;
            _permSvc       = permSvc;
            _userManager   = userManager;
        }

        // ── Budget Summary ────────────────────────────────────────────────────

        /// <summary>GET api/budget/{projectId} — fetch consolidated budget summary.</summary>
        [HttpGet("{projectId:int}")]
        public async Task<IActionResult> GetBudgetSummary(int projectId)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetView))
                return Forbid();

            try
            {
                var summary = await _budgetService.GetBudgetSummaryAsync(projectId);
                return Ok(summary);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>GET api/budget/{projectId}/derived — live bottom-up forecast.</summary>
        [HttpGet("{projectId:int}/derived")]
        public async Task<IActionResult> GetDerivedForecast(int projectId)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetView))
                return Forbid();

            try
            {
                var forecast = await _budgetService.GetDerivedBudgetForecastAsync(projectId);
                return Ok(forecast);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── Budget Baseline Set/Update ─────────────────────────────────────────

        /// <summary>POST api/budget/{projectId} — set or update the project budget baseline.</summary>
        [HttpPost("{projectId:int}")]
        public async Task<IActionResult> SetBudget(int projectId, [FromBody] SetBudgetRequest req)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetManage))
                return Forbid();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var currentUser = await _userManager.GetUserAsync(User);

            try
            {
                await _budgetService.SetProjectBudgetAsync(
                    projectId,
                    req.BudgetMode,
                    req.ApprovedBudget,
                    req.ContingencyReserve,
                    currentUser?.Id);

                var summary = await _budgetService.GetBudgetSummaryAsync(projectId);
                return Ok(summary);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── Advisory ──────────────────────────────────────────────────────────

        /// <summary>
        /// GET api/budget/{projectId}/advisory?additionalCost=N
        /// Returns advisory before saving a resource allocation.
        /// </summary>
        [HttpGet("{projectId:int}/advisory")]
        public async Task<IActionResult> GetAdvisory(int projectId, [FromQuery] decimal additionalCost = 0)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetView))
                return Forbid();

            try
            {
                var advisory = await _budgetService.GetBudgetAdvisoryAsync(projectId, additionalCost);
                return Ok(advisory);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── Other Costs ───────────────────────────────────────────────────────

        /// <summary>GET api/budget/{projectId}/othercosts — list non-labour cost items.</summary>
        [HttpGet("{projectId:int}/othercosts")]
        public async Task<IActionResult> GetOtherCosts(int projectId)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetView))
                return Forbid();

            var costs = await _budgetService.GetOtherCostsAsync(projectId);
            return Ok(costs);
        }

        /// <summary>POST api/budget/{projectId}/othercosts — add a non-labour cost item.</summary>
        [HttpPost("{projectId:int}/othercosts")]
        public async Task<IActionResult> AddOtherCost(int projectId, [FromBody] OtherCostUpsertDto dto)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetManage))
                return Forbid();

            dto.ProjectId = projectId;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUser = await _userManager.GetUserAsync(User);
            var cost = await _budgetService.AddOtherCostAsync(dto, currentUser?.Id);
            return CreatedAtAction(nameof(GetOtherCosts), new { projectId }, cost);
        }

        /// <summary>PUT api/budget/{projectId}/othercosts/{id} — update a non-labour cost item.</summary>
        [HttpPut("{projectId:int}/othercosts/{id:int}")]
        public async Task<IActionResult> UpdateOtherCost(int projectId, int id, [FromBody] OtherCostUpsertDto dto)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetManage))
                return Forbid();

            dto.ProjectId = projectId;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var cost = await _budgetService.UpdateOtherCostAsync(id, dto);
                return Ok(cost);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>DELETE api/budget/{projectId}/othercosts/{id} — delete a non-labour cost item.</summary>
        [HttpDelete("{projectId:int}/othercosts/{id:int}")]
        public async Task<IActionResult> DeleteOtherCost(int projectId, int id)
        {
            if (!await _permSvc.HasPermissionAsync(User, Permissions.BudgetManage))
                return Forbid();

            try
            {
                await _budgetService.DeleteOtherCostAsync(id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }

    // ── Request Models ────────────────────────────────────────────────────────

    public class SetBudgetRequest
    {
        public BudgetMode BudgetMode { get; set; } = BudgetMode.NotSet;
        public decimal? ApprovedBudget { get; set; }
        public decimal? ContingencyReserve { get; set; }
    }
}
