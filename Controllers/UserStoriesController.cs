using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class UserStoriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UserStoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: UserStories
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.UserStories.Include(u => u.CreatedBy).Include(u => u.Feature);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: UserStories/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userStory = await _context.UserStories
                .Include(u => u.CreatedBy)
                .Include(u => u.Feature)
                .Include(u => u.Tasks)
                .Include(u => u.TestCases)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userStory == null)
            {
                return NotFound();
            }

            return View(userStory);
        }

        // GET: UserStories/Create
        public IActionResult Create()
        {
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name");
            return View();
        }

        // POST: UserStories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,FeatureId,Title,Description,AcceptanceCriteria,Priority")] UserStory userStory)
        {
            if (ModelState.IsValid)
            {
                userStory.CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier);
                userStory.CreatedAt = DateTime.UtcNow;
                _context.Add(userStory);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", userStory.FeatureId);
            return View(userStory);
        }

        // GET: UserStories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userStory = await _context.UserStories.FindAsync(id);
            if (userStory == null)
            {
                return NotFound();
            }
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", userStory.FeatureId);
            return View(userStory);
        }

        // POST: UserStories/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,FeatureId,Title,Description,AcceptanceCriteria,Priority")] UserStory userStory)
        {
            if (id != userStory.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.UserStories.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                    if (existing == null) return NotFound();
                    
                    userStory.CreatedById = existing.CreatedById;
                    userStory.CreatedAt = existing.CreatedAt;
                    
                    _context.Update(userStory);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserStoryExists(userStory.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", userStory.FeatureId);
            return View(userStory);
        }

        // GET: UserStories/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userStory = await _context.UserStories
                .Include(u => u.CreatedBy)
                .Include(u => u.Feature)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userStory == null)
            {
                return NotFound();
            }

            return View(userStory);
        }

        // POST: UserStories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userStory = await _context.UserStories.FindAsync(id);
            if (userStory != null)
            {
                _context.UserStories.Remove(userStory);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool UserStoryExists(int id)
        {
            return _context.UserStories.Any(e => e.Id == id);
        }
    }
}
