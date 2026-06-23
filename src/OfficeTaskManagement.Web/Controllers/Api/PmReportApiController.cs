using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Services.Ai;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/pmreport")]
    [ApiController]
    [Authorize]
    public class PmReportApiController : ControllerBase
    {
        private readonly PmReportService _pmReport;

        public PmReportApiController(PmReportService pmReport)
        {
            _pmReport = pmReport;
        }

        // GET /api/pmreport/download/{projectId}
        [HttpGet("download/{projectId}")]
        public async Task<IActionResult> DownloadPdf(int projectId)
        {
            try
            {
                byte[] pdfBytes = await _pmReport.GeneratePdfReportAsync(projectId);
                
                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    return NotFound($"Could not generate status report for project #{projectId}.");
                }

                return File(pdfBytes, "application/pdf", $"Project_{projectId}_Status_Report.pdf");
            }
            catch (System.Exception ex)
            {
                return BadRequest($"PDF generation failed: {ex.Message}");
            }
        }
    }
}
