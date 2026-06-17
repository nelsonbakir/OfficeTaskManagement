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
    [Authorize]
    public class ProjectsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;
        private readonly IBudgetService _budgetService;

        public ProjectsController(
            ApplicationDbContext context,
            IMediaService mediaService,
            IBudgetService budgetService)
        {
            _context       = context;
            _mediaService  = mediaService;
            _budgetService = budgetService;
        }

        // GET: Projects
        public async Task<IActionResult> Index([FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Projects.Include(p => p.CreatedBy).AsQueryable();

            var canSeeAll = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) || await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage);
            if (!canSeeAll)
            {
                var isLead = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
                if (isLead)
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
        public async Task<IActionResult> Details(int? id, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
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
                .Include(p => p.ResourceAllocations)
                    .ThenInclude(a => a.User)
                .AsQueryable();

            var canSeeAll = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) || await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage);
            if (!canSeeAll)
            {
                var isLead = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
                if (isLead)
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

        // GET: Projects/Budget/5
        [HasPermission(Permissions.BudgetView)]
        public async Task<IActionResult> Budget(int? id)
        {
            if (id == null) return NotFound();

            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            try
            {
                var summary = await _budgetService.GetBudgetSummaryAsync(id.Value);
                ViewBag.ProjectId = id;
                ViewBag.ProjectName = project.Name;
                return View(summary);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        // GET: Projects/Create
        [HasPermission(Permissions.ProjectsManage)]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Projects/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ProjectsManage)]
        public async Task<IActionResult> Create([Bind(Prefix = "")] ProjectViewModel vm)
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
        [HasPermission(Permissions.ProjectsManage)]
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
        [HasPermission(Permissions.ProjectsManage)]
        public async Task<IActionResult> Edit(int id, [Bind(Prefix = "")] ProjectViewModel vm)
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
                        existingProject.RequiredSkills = vm.Project.RequiredSkills;
                        existingProject.RepositoryPath = vm.Project.RepositoryPath;
                        existingProject.RepositoryUrl = vm.Project.RepositoryUrl;

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
        [HasPermission(Permissions.ProjectsManage)]
        public async Task<IActionResult> DeleteAttachment(int id, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var projectId = attachment.ProjectId;
            if (projectId == null) return BadRequest();

            // Access check
            var isLeadOrAdmin = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
            if (attachment.UploadedById != userId && !isLeadOrAdmin)
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = projectId.Value });
        }

        // GET: Projects/Delete/5
        [HasPermission(Permissions.ProjectsManage)]
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
        [HasPermission(Permissions.ProjectsManage)]
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

        // GET: Projects/OnboardWizard/5
        public async Task<IActionResult> OnboardWizard(int? id)
        {
            if (id == null) return NotFound();

            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            return View(project);
        }

        private bool ProjectExists(int id)
        {
            return _context.Projects.Any(e => e.Id == id);
        }
    }
}
