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
    public class EpicsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public EpicsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Epics
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Epics
                .Include(e => e.CreatedBy)
                .Include(e => e.Project)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(e => e.Project.CreatedById == userId ||
                                             e.Project.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             e.Project.Epics.Any(ep => ep.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                }
            }

            return View(await query.ToListAsync());
        }

        // GET: Epics/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Epics
                .Include(e => e.CreatedBy)
                .Include(e => e.Project)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(e => e.Project.CreatedById == userId ||
                                             e.Project.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             e.Project.Epics.Any(ep => ep.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                }
            }

            var epic = await query.FirstOrDefaultAsync(m => m.Id == id);
            if (epic == null)
            {
                return NotFound();
            }

            return View(epic);
        }

        // GET: Epics/Create
        [Authorize(Roles = "Manager,Project Lead")]
        public IActionResult Create()
        {
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id");
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name");
            return View();
        }

        // POST: Epics/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Create([Bind("Id,ProjectId,Name,Description,CreatedById,CreatedAt")] Epic epic)
        {
            if (ModelState.IsValid)
            {
                _context.Add(epic);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", epic.CreatedById);
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", epic.ProjectId);
            return View(epic);
        }

        // GET: Epics/Edit/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var epic = await _context.Epics.FindAsync(id);
            if (epic == null)
            {
                return NotFound();
            }
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", epic.CreatedById);
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", epic.ProjectId);
            return View(epic);
        }

        // POST: Epics/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,ProjectId,Name,Description,CreatedById,CreatedAt")] Epic epic)
        {
            if (id != epic.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(epic);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EpicExists(epic.Id))
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
            ViewData["CreatedById"] = new SelectList(_context.Users, "Id", "Id", epic.CreatedById);
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", epic.ProjectId);
            return View(epic);
        }

        // GET: Epics/Delete/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var epic = await _context.Epics
                .Include(e => e.CreatedBy)
                .Include(e => e.Project)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (epic == null)
            {
                return NotFound();
            }

            return View(epic);
        }

        // POST: Epics/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var epic = await _context.Epics.FindAsync(id);
            if (epic != null)
            {
                _context.Epics.Remove(epic);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool EpicExists(int id)
        {
            return _context.Epics.Any(e => e.Id == id);
        }
    }
}
