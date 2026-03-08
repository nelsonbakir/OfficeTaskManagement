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
    public class FeaturesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;

        public FeaturesController(ApplicationDbContext context, IMediaService mediaService)
        {
            _context = context;
            _mediaService = mediaService;
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
                .Include(f => f.Attachments)
                    .ThenInclude(a => a.UploadedBy)
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Create(FeatureViewModel vm)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                vm.Feature.CreatedById = userId;
                vm.Feature.CreatedAt = DateTime.UtcNow;

                _context.Add(vm.Feature);
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
                                FeatureId = vm.Feature.Id,
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
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", vm.Feature.EpicId);
            return View(vm);
        }

        // GET: Features/Edit/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feature = await _context.Features
                .Include(f => f.Tasks)
                .Include(f => f.Attachments)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (feature == null)
            {
                return NotFound();
            }
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", feature.EpicId);
            return View(new FeatureViewModel { Feature = feature });
        }

        // POST: Features/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int id, FeatureViewModel vm)
        {
            if (id != vm.Feature.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingFeature = await _context.Features
                        .Include(f => f.Attachments)
                        .FirstOrDefaultAsync(f => f.Id == id);

                    if (existingFeature != null)
                    {
                        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                        existingFeature.Name = vm.Feature.Name;
                        existingFeature.Description = vm.Feature.Description;
                        existingFeature.EpicId = vm.Feature.EpicId;

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
                                        FeatureId = existingFeature.Id,
                                        FileName = file.FileName,
                                        FilePath = filePath,
                                        UploadedById = userId,
                                        UploadedAt = DateTime.UtcNow
                                    });
                                }
                            }
                        }

                        _context.Update(existingFeature);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!FeatureExists(vm.Feature.Id))
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
            ViewData["EpicId"] = new SelectList(_context.Epics, "Id", "Name", vm.Feature.EpicId);
            return View(vm);
        }

        [HttpPost]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var featureId = attachment.FeatureId;
            if (featureId == null) return BadRequest();

            // Access check
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = featureId.Value });
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
