using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Project Lead,Project Coordinator,Employee")]
    public class FeaturesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FeaturesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Features
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Features
                .Include(f => f.CreatedBy)
                .Include(f => f.Epic)
                    .ThenInclude(e => e.Project)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(f => f.Epic.Project.CreatedById == userId ||
                                             f.Epic.Project.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             f.Epic.Project.Epics.Any(ep => ep.Features.Any(fe => fe.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
            }

            return View(await query.ToListAsync());
        }

        // GET: Features/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Features
                .Include(f => f.CreatedBy)
                .Include(f => f.Epic)
                    .ThenInclude(e => e.Project)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(f => f.Epic.Project.CreatedById == userId ||
                                             f.Epic.Project.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             f.Epic.Project.Epics.Any(ep => ep.Features.Any(fe => fe.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
            }

            var feature = await query.FirstOrDefaultAsync(m => m.Id == id);
            if (feature == null)
            {
                return NotFound();
            }

            return View(feature);
        }

        // GET: Features/Create
        [Authorize(Roles = "Manager,Project Lead")]
        public IActionResult Create()
        {
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id");
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name");
            return View();
        }

        // POST: Features/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Create([Bind("Id,EpicId,Name,Description,CreatedById,CreatedAt")] Feature feature)
        {
            if (ModelState.IsValid)
            {
                _context.Add(feature);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", feature.CreatedById);
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", feature.EpicId);
            return View(feature);
        }

        // GET: Features/Edit/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feature = await _context.Features.FindAsync(id);
            if (feature == null)
            {
                return NotFound();
            }
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", feature.CreatedById);
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", feature.EpicId);
            return View(feature);
        }

        // POST: Features/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,EpicId,Name,Description,CreatedById,CreatedAt")] Feature feature)
        {
            if (id != feature.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(feature);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!FeatureExists(feature.Id))
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
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", feature.CreatedById);
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", feature.EpicId);
            return View(feature);
        }

        // GET: Features/Delete/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feature = await _context.Features
                .Include(f => f.CreatedBy)
                .Include(f => f.Epic)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (feature == null)
            {
                return NotFound();
            }

            return View(feature);
        }

        // POST: Features/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var feature = await _context.Features.FindAsync(id);
            if (feature != null)
            {
                _context.Features.Remove(feature);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool FeatureExists(int id)
        {
            return _context.Features.Any(e => e.Id == id);
        }
    }
}
