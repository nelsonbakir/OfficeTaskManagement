using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Services;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/analytics")]
    [ApiController]
    [Authorize(Roles = "Manager,Admin,Project Lead,Project Coordinator")]
    public class AnalyticsApiController : ControllerBase
    {
        private readonly IGeminiAnalyticsService _geminiService;

        public AnalyticsApiController(IGeminiAnalyticsService geminiService)
        {
            _geminiService = geminiService;
        }

        [HttpGet("ai-insights/burnout")]
        public async Task<IActionResult> GetBurnoutInsight()
        {
            var result = await _geminiService.DetectBurnoutAsync();
            return Ok(new { report = result });
        }

        [HttpGet("ai-insights/retrospective")]
        public async Task<IActionResult> GetRetrospectiveInsight()
        {
            var result = await _geminiService.GenerateSprintRetrospectiveAsync();
            return Ok(new { report = result });
        }

        [HttpGet("ai-insights/technical-debt")]
        public async Task<IActionResult> GetTechnicalDebtInsight()
        {
            var result = await _geminiService.AnalyzeTechnicalDebtAsync();
            return Ok(new { report = result });
        }

        [HttpGet("ai-insights/project-predictability/{projectId}")]
        public async Task<IActionResult> GetProjectPredictabilityInsight(int projectId)
        {
            var result = await _geminiService.PredictProjectDelayAsync(projectId);
            return Ok(new { report = result });
        }
    }
}
