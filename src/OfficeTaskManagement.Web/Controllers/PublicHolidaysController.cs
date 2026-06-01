using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Admin")]
    public class PublicHolidaysController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PublicHolidaysController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var holidays = await _context.PublicHolidays.OrderByDescending(h => h.Date).ToListAsync();
            return View(holidays);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Date,IsFixedDate")] PublicHoliday publicHoliday)
        {
            if (ModelState.IsValid)
            {
                _context.Add(publicHoliday);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Holiday added successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to add holiday. Please check your inputs.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var holiday = await _context.PublicHolidays.FindAsync(id);
            if (holiday != null)
            {
                _context.PublicHolidays.Remove(holiday);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        public IActionResult DownloadSampleExcel()
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Public Holidays");
            
            worksheet.Cell(1, 1).Value = "Name";
            worksheet.Cell(1, 2).Value = "Date (YYYY-MM-DD)";
            worksheet.Cell(1, 3).Value = "IsFixedDate (true/false)";

            worksheet.Cell(2, 1).Value = "New Year's Day";
            worksheet.Cell(2, 2).Value = new System.DateTime(System.DateTime.UtcNow.Year, 1, 1).ToString("yyyy-MM-dd");
            worksheet.Cell(2, 3).Value = "true";

            worksheet.Cell(3, 1).Value = "Labor Day";
            worksheet.Cell(3, 2).Value = new System.DateTime(System.DateTime.UtcNow.Year, 5, 1).ToString("yyyy-MM-dd");
            worksheet.Cell(3, 3).Value = "true";

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PublicHolidaySample.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> BulkCreate([FromBody] List<PublicHoliday> holidays)
        {
            if (holidays == null || holidays.Count == 0)
            {
                return BadRequest("No data provided.");
            }

            foreach(var h in holidays)
            {
                // Ensure default values or cleanup if needed
                h.Id = 0; // Prevent identity insert issues
                h.CreatedAt = System.DateTime.UtcNow;
            }

            _context.PublicHolidays.AddRange(holidays);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}
