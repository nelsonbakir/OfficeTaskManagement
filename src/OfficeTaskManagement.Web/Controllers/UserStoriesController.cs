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
using OfficeTaskManagement.ViewModels;
using OfficeTaskManagement.Services;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class UserStoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;

        public UserStoriesController(ApplicationDbContext context, IMediaService mediaService)
        {
            _context = context;
            _mediaService = mediaService;
        }

        // GET: UserStories
        public async Task<IActionResult> Index(int? projectId, int? epicId, int? featureId)
        {
            ViewBag.ProjectId = new SelectList(_context.Projects, "Id", "Name", projectId);
            
            var epicsQuery = _context.Epics.AsQueryable();
            if (projectId.HasValue) epicsQuery = epicsQuery.Where(e => e.ProjectId == projectId.Value);
            ViewBag.EpicId = new SelectList(epicsQuery, "Id", "Name", epicId);
            
            var featuresQuery = _context.Features.AsQueryable();
            if (epicId.HasValue) featuresQuery = featuresQuery.Where(f => f.EpicId == epicId.Value);
            else if (projectId.HasValue) featuresQuery = featuresQuery.Where(f => f.Epic.ProjectId == projectId.Value);
            ViewBag.FeatureId = new SelectList(featuresQuery, "Id", "Name", featureId);

            var query = _context.UserStories
                .Include(u => u.CreatedBy)
                .Include(u => u.Feature)
                    .ThenInclude(f => f.Epic)
                        .ThenInclude(e => e.Project)
                .AsQueryable();

            if (projectId.HasValue) query = query.Where(u => u.Feature.Epic.ProjectId == projectId.Value);
            if (epicId.HasValue) query = query.Where(u => u.Feature.EpicId == epicId.Value);
            if (featureId.HasValue) query = query.Where(u => u.FeatureId == featureId.Value);

            return View(await query.ToListAsync());
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
                .Include(u => u.Attachments)
                    .ThenInclude(a => a.UploadedBy)
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
        public async Task<IActionResult> Create(UserStoryViewModel vm)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                vm.UserStory.CreatedById = userId;
                vm.UserStory.CreatedAt = DateTime.UtcNow;

                _context.Add(vm.UserStory);
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
                                UserStoryId = vm.UserStory.Id,
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
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", vm.UserStory.FeatureId);
            return View(vm);
        }

        // GET: UserStories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userStory = await _context.UserStories
                .Include(u => u.Attachments)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userStory == null)
            {
                return NotFound();
            }
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", userStory.FeatureId);
            return View(new UserStoryViewModel { UserStory = userStory });
        }

        // POST: UserStories/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UserStoryViewModel vm)
        {
            if (id != vm.UserStory.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.UserStories
                        .Include(u => u.Attachments)
                        .FirstOrDefaultAsync(u => u.Id == id);
                    if (existing == null) return NotFound();
                    
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    existing.Title = vm.UserStory.Title;
                    existing.Description = vm.UserStory.Description;
                    existing.AcceptanceCriteria = vm.UserStory.AcceptanceCriteria;
                    existing.FeatureId = vm.UserStory.FeatureId;
                    existing.Priority = vm.UserStory.Priority;
                    
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
                                    UserStoryId = existing.Id,
                                    FileName = file.FileName,
                                    FilePath = filePath,
                                    UploadedById = userId,
                                    UploadedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }

                    _context.Update(existing);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserStoryExists(vm.UserStory.Id))
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
            ViewData["FeatureId"] = new SelectList(_context.Features, "Id", "Name", vm.UserStory.FeatureId);
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var userStoryId = attachment.UserStoryId;
            if (userStoryId == null) return BadRequest();

            // Access check
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = userStoryId.Value });
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
