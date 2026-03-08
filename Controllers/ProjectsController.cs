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
using OfficeTaskManagement.Services;
using OfficeTaskManagement.ViewModels;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Project Lead,Project Coordinator,Employee")]
    public class ProjectsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;

        public ProjectsController(ApplicationDbContext context, IMediaService mediaService)
        {
            _context = context;
            _mediaService = mediaService;
        }

        // GET: Projects
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Projects.Include(p => p.CreatedBy).AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(p => p.CreatedById == userId || p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) || p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(p => p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) || p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
            }

            return View(await query.ToListAsync());
        }

        // GET: Projects/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Projects
                .Include(p => p.CreatedBy)
                .Include(p => p.Attachments)
                    .ThenInclude(a => a.UploadedBy)
                .Include(p => p.Epics)
                .Include(p => p.Sprints)
                    .ThenInclude(s => s.Tasks)
                        .ThenInclude(t => t.Assignee)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(p => p.CreatedById == userId || p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) || p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(p => p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) || p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
            }

            var project = await query.FirstOrDefaultAsync(m => m.Id == id);
            
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // GET: Projects/Create
        [Authorize(Roles = "Manager,Project Lead")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Projects/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Create(ProjectViewModel vm)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                vm.Project.CreatedById = userId;
                vm.Project.CreatedAt = DateTime.UtcNow;

                // Handle Logo
                if (vm.Logo != null)
                {
                    using (var stream = vm.Logo.OpenReadStream())
                    {
                        vm.Project.LogoPath = await _mediaService.UploadAsync(stream, vm.Logo.FileName, vm.Logo.ContentType);
                    }
                }

                _context.Add(vm.Project);
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
                                ProjectId = vm.Project.Id,
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
            return View(vm);
        }

        // GET: Projects/Edit/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var project = await _context.Projects
                .Include(p => p.Attachments)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (project == null)
            {
                return NotFound();
            }
            return View(new ProjectViewModel { Project = project });
        }

        // POST: Projects/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Edit(int id, ProjectViewModel vm)
        {
            if (id != vm.Project.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingProject = await _context.Projects
                        .Include(p => p.Attachments)
                        .FirstOrDefaultAsync(p => p.Id == id);

                    if(existingProject != null)
                    {
                        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                        
                        existingProject.Name = vm.Project.Name;
                        existingProject.Description = vm.Project.Description;

                        // Handle Logo Update
                        if (vm.Logo != null)
                        {
                            if (!string.IsNullOrEmpty(existingProject.LogoPath))
                            {
                                await _mediaService.DeleteAsync(existingProject.LogoPath);
                            }
                            using (var stream = vm.Logo.OpenReadStream())
                            {
                                existingProject.LogoPath = await _mediaService.UploadAsync(stream, vm.Logo.FileName, vm.Logo.ContentType);
                            }
                        }

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
                                        ProjectId = existingProject.Id,
                                        FileName = file.FileName,
                                        FilePath = filePath,
                                        UploadedById = userId,
                                        UploadedAt = DateTime.UtcNow
                                    });
                                }
                            }
                        }

                        _context.Update(existingProject);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProjectExists(vm.Project.Id))
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
            return View(vm);
        }

        [HttpPost]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var projectId = attachment.ProjectId;
            if (projectId == null) return BadRequest();

            // Access check
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = projectId.Value });
        }

        // GET: Projects/Delete/5
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var project = await _context.Projects
                .Include(p => p.CreatedBy)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // POST: Projects/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Project Lead")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project != null)
            {
                _context.Projects.Remove(project);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ProjectExists(int id)
        {
            return _context.Projects.Any(e => e.Id == id);
        }
    }
}
