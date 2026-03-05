using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.ViewModels;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class TaskItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public TaskItemsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: TaskItems
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Feature)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(t => t.AssigneeId == userId || 
                                             t.CreatedById == userId ||
                                             (t.Project != null && (t.Project.CreatedById == userId || t.Project.Sprints.Any(s => s.Tasks.Any(task => task.AssigneeId == userId || task.CreatedById == userId)) || t.Project.Epics.Any(ep => ep.Features.Any(fe => fe.Tasks.Any(task => task.AssigneeId == userId || task.CreatedById == userId))))));
                }
                else
                {
                    query = query.Where(t => t.AssigneeId == userId || t.CreatedById == userId);
                }
            }
            
            return View(await query.ToListAsync());
        }

        // GET: TaskItems/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Feature)
                .Include(t => t.History).ThenInclude(h => h.ChangedBy)
                .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
                .AsQueryable();

            if (!User.IsInRole("Manager") && !User.IsInRole("Project Coordinator"))
            {
                if (User.IsInRole("Project Lead"))
                {
                    query = query.Where(t => t.AssigneeId == userId || 
                                             t.CreatedById == userId ||
                                             (t.Project != null && (t.Project.CreatedById == userId || t.Project.Sprints.Any(s => s.Tasks.Any(task => task.AssigneeId == userId || task.CreatedById == userId)) || t.Project.Epics.Any(ep => ep.Features.Any(fe => fe.Tasks.Any(task => task.AssigneeId == userId || task.CreatedById == userId))))));
                }
                else
                {
                    query = query.Where(t => t.AssigneeId == userId || t.CreatedById == userId);
                }
            }
            var taskItem = await query.FirstOrDefaultAsync(m => m.Id == id);
            if (taskItem == null)
            {
                return NotFound();
            }

            return View(taskItem);
        }

        // GET: TaskItems/Create
        public IActionResult Create()
        {
            var vm = new TaskItemViewModel
            {
                UsersList = new SelectList(_context.Users, "Id", "Email"),
                ProjectsList = new SelectList(_context.Projects, "Id", "Name"),
                SprintsList = new SelectList(_context.Sprints, "Id", "Name"),
                FeaturesList = new SelectList(_context.Features, "Id", "Name")
            };
            return View(vm);
        }

        // POST: TaskItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskItemViewModel vm)
        {
            if (ModelState.IsValid)
            {
                vm.TaskItem.CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier);
                vm.TaskItem.CreatedAt = DateTime.UtcNow;

                // Normalize date times to UTC to satisfy PostgreSQL timestamptz requirements
                vm.TaskItem.StartDate = EnsureUtc(vm.TaskItem.StartDate);
                vm.TaskItem.DueDate = EnsureUtc(vm.TaskItem.DueDate);

                _context.Add(vm.TaskItem);
                await _context.SaveChangesAsync();

                // Add to History
                _context.TaskHistories.Add(new TaskHistory
                {
                    TaskItemId = vm.TaskItem.Id,
                    ChangedById = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    ChangeDescription = "Task created.",
                    Timestamp = DateTime.UtcNow
                });

                // Handle Attachment
                if (vm.Attachment != null)
                {
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt" };
                    var extension = Path.GetExtension(vm.Attachment.FileName).ToLowerInvariant();
                    
                    if (allowedExtensions.Contains(extension))
                    {
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(vm.Attachment.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await vm.Attachment.CopyToAsync(fileStream);
                        }

                        _context.TaskAttachments.Add(new TaskAttachment
                        {
                            TaskItemId = vm.TaskItem.Id,
                            FileName = vm.Attachment.FileName,
                            FilePath = "/uploads/" + uniqueFileName,
                            UploadedById = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        });
                    }
                    else
                    {
                        ModelState.AddModelError("Attachment", "Invalid file type.");
                        vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
                        vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                        vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                        vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                        return View(vm);
                    }
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            
            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
            return View(vm);
        }

        // GET: TaskItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var taskItem = await _context.Tasks.FindAsync(id);
            if (taskItem == null)
            {
                return NotFound();
            }
            
            var vm = new TaskItemViewModel
            {
                TaskItem = taskItem,
                UsersList = new SelectList(_context.Users, "Id", "Email", taskItem.AssigneeId),
                ProjectsList = new SelectList(_context.Projects, "Id", "Name", taskItem.ProjectId),
                SprintsList = new SelectList(_context.Sprints, "Id", "Name", taskItem.SprintId),
                FeaturesList = new SelectList(_context.Features, "Id", "Name", taskItem.FeatureId)
            };
            return View(vm);
        }

        // POST: TaskItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TaskItemViewModel vm)
        {
            if (id != vm.TaskItem.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingTask = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
                    if (existingTask == null) return NotFound();

                    // Ensure existing and incoming DateTimes are normalized to UTC
                    existingTask.CreatedAt = EnsureUtc(existingTask.CreatedAt);
                    vm.TaskItem.StartDate = EnsureUtc(vm.TaskItem.StartDate);
                    vm.TaskItem.DueDate = EnsureUtc(vm.TaskItem.DueDate);

                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var userRole = User.IsInRole("Project Lead") || User.IsInRole("Manager");
                    
                    // Logic to enforce who can mark as done
                    if (vm.TaskItem.Status == TaskStatus.Done)
                    {
                        if (!userRole && existingTask.CreatedById != userId)
                        {
                            ModelState.AddModelError("", "Only Project Lead, Manager, or Task Owner can mark as done.");
                            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            return View(vm);
                        }
                    }

                    // Log history for what changed
                    var changes = new List<string>();
                    if (existingTask.Title != vm.TaskItem.Title) changes.Add("Title updated.");
                    if (existingTask.Status != vm.TaskItem.Status) changes.Add($"Status changed from {existingTask.Status} to {vm.TaskItem.Status}.");
                    if (existingTask.AssigneeId != vm.TaskItem.AssigneeId) changes.Add("Assignee changed.");
                    if (existingTask.EstimatedHours != vm.TaskItem.EstimatedHours) changes.Add("Estimated Hours changed.");
                    if (existingTask.StartDate != vm.TaskItem.StartDate) changes.Add("Start Date changed.");
                    
                    if (changes.Any())
                    {
                        _context.TaskHistories.Add(new TaskHistory
                        {
                            TaskItemId = vm.TaskItem.Id,
                            ChangedById = userId,
                            ChangeDescription = string.Join(" ", changes),
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    existingTask.Title = vm.TaskItem.Title;
                    existingTask.Description = vm.TaskItem.Description;
                    existingTask.Status = vm.TaskItem.Status;
                    existingTask.EstimatedHours = vm.TaskItem.EstimatedHours;
                    existingTask.StartDate = vm.TaskItem.StartDate;
                    existingTask.DueDate = vm.TaskItem.DueDate;
                    existingTask.ProjectId = vm.TaskItem.ProjectId;
                    existingTask.SprintId = vm.TaskItem.SprintId;
                    existingTask.FeatureId = vm.TaskItem.FeatureId;
                    existingTask.AssigneeId = vm.TaskItem.AssigneeId;

                    _context.Update(existingTask);

                    // Handle Attachment
                    if (vm.Attachment != null)
                    {
                        string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt" };
                        var extension = Path.GetExtension(vm.Attachment.FileName).ToLowerInvariant();
                        
                        if (allowedExtensions.Contains(extension))
                        {
                            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(vm.Attachment.FileName);
                            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                            using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await vm.Attachment.CopyToAsync(fileStream);
                            }

                            _context.TaskAttachments.Add(new TaskAttachment
                            {
                                TaskItemId = vm.TaskItem.Id,
                                FileName = vm.Attachment.FileName,
                                FilePath = "/uploads/" + uniqueFileName,
                                UploadedById = userId
                            });
                            
                            _context.TaskHistories.Add(new TaskHistory
                            {
                                TaskItemId = vm.TaskItem.Id,
                                ChangedById = userId,
                                ChangeDescription = "Attachment added."
                            });
                        }
                        else
                        {
                            ModelState.AddModelError("Attachment", "Invalid file type.");
                            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            return View(vm);
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TaskItemExists(vm.TaskItem.Id))
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
            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
            return View(vm);
        }

        // GET: TaskItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var taskItem = await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Feature)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (taskItem == null)
            {
                return NotFound();
            }

            return View(taskItem);
        }

        // POST: TaskItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var taskItem = await _context.Tasks.FindAsync(id);
            if (taskItem != null)
            {
                _context.Tasks.Remove(taskItem);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TaskItemExists(int id)
        {
            return _context.Tasks.Any(e => e.Id == id);
        }

        private static DateTime EnsureUtc(DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            };
        }

        private static DateTime? EnsureUtc(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            var d = dt.Value;
            return d.Kind switch
            {
                DateTimeKind.Utc => d,
                DateTimeKind.Local => d.ToUniversalTime(),
                _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            };
        }
    }
}
