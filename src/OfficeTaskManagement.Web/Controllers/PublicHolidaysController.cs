using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager")]
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

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Date,IsFixedDate")] PublicHoliday publicHoliday)
        {
            if (ModelState.IsValid)
            {
                _context.Add(publicHoliday);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(publicHoliday);
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
    }
}
