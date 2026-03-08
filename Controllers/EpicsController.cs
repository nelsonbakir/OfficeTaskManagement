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
using OfficeTaskManagement.ViewModels;
using OfficeTaskManagement.Services;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Project Lead,Project Coordinator,Employee")]
    public class EpicsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;

        public EpicsController(ApplicationDbContext context, IMediaService mediaService)
        {
            _context = context;
            _mediaService = mediaService;
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
                .Include(e => e.Attachments)
                    .ThenInclude(a => a.UploadedBy)
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Create(EpicViewModel vm)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                vm.Epic.CreatedById = userId;
                vm.Epic.CreatedAt = DateTime.UtcNow;

                _context.Add(vm.Epic);
                await _context.SaveChangesAsync();

                // Handle Attachments
                if (vm.Attachments != null && vm.Attachments.Any())
                {
                    foreach (var file in vm.Attachments)
                    {
                        using (var stream = file.OpenReadStream())
                        {
                            var filePath = await _mediaService.UploadAsync(stream, file.FileName, file.ContentType);
                            _context.Attachments.Add(new Attachment
                            {
                                EpicId = vm.Epic.Id,
                                FileName = file.FileName,
                                FilePath = filePath,
                                UploadedById = userId,
                                UploadedAt = DateTime.UtcNow
                            });
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                return RedirectToAction(nameof(Index));
            }
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", vm.Epic.ProjectId);
            return View(vm);
        }

        // GET: Epics/Edit/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var epic = await _context.Epics
                .Include(e => e.Features)
                    .ThenInclude(f => f.Tasks)
                .Include(e => e.Attachments)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (epic == null)
            {
                return NotFound();
            }
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", epic.ProjectId);
            return View(new EpicViewModel { Epic = epic });
        }

        // POST: Epics/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int id, EpicViewModel vm)
        {
            if (id != vm.Epic.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingEpic = await _context.Epics
                        .Include(e => e.Attachments)
                        .FirstOrDefaultAsync(e => e.Id == id);

                    if (existingEpic != null)
                    {
                        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                        existingEpic.Name = vm.Epic.Name;
                        existingEpic.Description = vm.Epic.Description;
                        existingEpic.ProjectId = vm.Epic.ProjectId;

                        // Handle New Attachments
                        if (vm.Attachments != null && vm.Attachments.Any())
                        {
                            foreach (var file in vm.Attachments)
                            {
                                using (var stream = file.OpenReadStream())
                                {
                                    var filePath = await _mediaService.UploadAsync(stream, file.FileName, file.ContentType);
                                    _context.Attachments.Add(new Attachment
                                    {
                                        EpicId = existingEpic.Id,
                                        FileName = file.FileName,
                                        FilePath = filePath,
                                        UploadedById = userId,
                                        UploadedAt = DateTime.UtcNow
                                    });
                                }
                            }
                        }

                        _context.Update(existingEpic);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EpicExists(vm.Epic.Id))
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
            ViewData["ProjectId"] = new SelectList(_context.Projects, "Id", "Name", vm.Epic.ProjectId);
            return View(vm);
        }

        [HttpPost]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var epicId = attachment.EpicId;
            if (epicId == null) return BadRequest();

            // Access check
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = epicId.Value });
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
