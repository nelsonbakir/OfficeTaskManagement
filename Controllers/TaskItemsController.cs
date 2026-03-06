using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
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
                .Include(t => t.History).ThenInclude(h => h.ChangedBy)
                .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
                .Include(t => t.SubTasks)
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
                FeaturesList = new SelectList(_context.Features, "Id", "Name"),
                ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null), "Id", "Title")
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
                
                // If this is a sub-task and its status is InProgress, ensure parent is also InProgress
                if (vm.TaskItem.ParentTaskId.HasValue && vm.TaskItem.Status == TaskStatus.InProgress)
                {
                    var parent = await _context.Tasks.FindAsync(vm.TaskItem.ParentTaskId);
                    if (parent != null && parent.Status != TaskStatus.InProgress)
                    {
                        parent.Status = TaskStatus.InProgress;
                        _context.Update(parent);
                    }
                }

                await _context.SaveChangesAsync();

                // Notify new assignee if someone else created the task
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId) && vm.TaskItem.AssigneeId != currentUserId)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = vm.TaskItem.AssigneeId,
                        Title = "New Task Assigned",
                        Message = $"You have been assigned: {vm.TaskItem.Title}",
                        Link = $"/TaskItems/Details/{vm.TaskItem.Id}",
                        Type = "Assignment"
                    });
                }

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
                        vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
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
            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
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
                FeaturesList = new SelectList(_context.Features, "Id", "Name", taskItem.FeatureId),
                ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != taskItem.Id), "Id", "Title", taskItem.ParentTaskId)
            };
            
            // Explicitly load SubTasks for the view
            await _context.Entry(taskItem).Collection(t => t.SubTasks).LoadAsync();
            
            // Explicitly load relations for the view
            await _context.Entry(taskItem).Collection(t => t.Attachments).LoadAsync();
            foreach (var attachment in taskItem.Attachments)
            {
                await _context.Entry(attachment).Reference(a => a.UploadedBy).LoadAsync();
            }

            await _context.Entry(taskItem).Collection(t => t.Comments).LoadAsync();
            foreach (var comment in taskItem.Comments)
            {
                await _context.Entry(comment).Reference(c => c.User).LoadAsync();
            }

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
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            return View(vm);
                        }

                        // Check Sub-tasks status: Cannot mark parent as Done if any sub-task is not Done
                        var hasOpenSubTasks = await _context.Tasks.AnyAsync(t => t.ParentTaskId == id && t.Status != TaskStatus.Done);
                        if (hasOpenSubTasks)
                        {
                            ModelState.AddModelError("", "Cannot mark this task as Done because it has open Sub-tasks. Please complete all Sub-tasks first.");
                            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            return View(vm);
                        }
                    }

                    // Log history and push notifications for what changed
                    var changes = new List<string>();
                    if (existingTask.Title != vm.TaskItem.Title) changes.Add("Title updated.");
                    if (existingTask.Status != vm.TaskItem.Status)
                    {
                        changes.Add($"Status changed from {existingTask.Status} to {vm.TaskItem.Status}.");
                        if (vm.TaskItem.Status == TaskStatus.Done && existingTask.CreatedById != userId)
                        {
                            _context.Notifications.Add(new Notification
                            {
                                UserId = existingTask.CreatedById,
                                Title = "Task Completed",
                                Message = $"Task '{vm.TaskItem.Title}' was marked as Done.",
                                Link = $"/TaskItems/Details/{existingTask.Id}",
                                Type = "StatusUpdate"
                            });
                        }
                    }
                    if (existingTask.AssigneeId != vm.TaskItem.AssigneeId)
                    {
                        changes.Add("Assignee changed.");
                        if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId) && vm.TaskItem.AssigneeId != userId)
                        {
                            _context.Notifications.Add(new Notification
                            {
                                UserId = vm.TaskItem.AssigneeId,
                                Title = "Task Assignment Updated",
                                Message = $"You have been assigned to: {vm.TaskItem.Title}",
                                Link = $"/TaskItems/Details/{existingTask.Id}",
                                Type = "Assignment"
                            });
                        }
                    }
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
                    existingTask.ParentTaskId = vm.TaskItem.ParentTaskId;
                    existingTask.Type = vm.TaskItem.Type;

                    _context.Update(existingTask);
                    
                    // If a sub-task is moving to InProgress, parent must be InProgress
                    if (existingTask.ParentTaskId.HasValue && existingTask.Status == TaskStatus.InProgress)
                    {
                        var parent = await _context.Tasks.FindAsync(existingTask.ParentTaskId);
                        if (parent != null && parent.Status != TaskStatus.InProgress && parent.Status != TaskStatus.Done)
                        {
                            parent.Status = TaskStatus.InProgress;
                            _context.Update(parent);
                            _context.TaskHistories.Add(new TaskHistory
                            {
                                TaskItemId = parent.Id,
                                ChangedById = userId,
                                ChangeDescription = $"Status changed to InProgress automatically because sub-task '{existingTask.Title}' started.",
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }

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
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
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
            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
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

        // POST: TaskItems/DeleteAttachment/5
        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var attachment = await _context.TaskAttachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var taskId = attachment.TaskItemId;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Access check: only the uploader, or Manager/ProjectLead can delete it
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            var filePath = Path.Combine(_env.WebRootPath, attachment.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            _context.TaskAttachments.Remove(attachment);
            
            _context.TaskHistories.Add(new TaskHistory
            {
                TaskItemId = taskId,
                ChangedById = userId,
                ChangeDescription = $"Attachment '{attachment.FileName}' deleted.",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = taskId });
        }

        // POST: TaskItems/AddComment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(int taskId, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return BadRequest();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var comment = new TaskComment
            {
                TaskId = taskId,
                UserId = userId,
                CommentText = text,
                CreatedAt = DateTime.UtcNow
            };

            _context.TaskComments.Add(comment);
            await _context.SaveChangesAsync();

            // Load user to return in partial view
            await _context.Entry(comment).Reference(c => c.User).LoadAsync();

            var formattedText = comment.CommentText.Replace("\n", "<br/>");
            // Highlight mentions: @(Some Name)
            formattedText = Regex.Replace(formattedText, @"@([A-Za-z0-9_\.\s]+)", "<span class='mention-badge'>@$1</span>");

            // Extract Mentions using Regex and push Notifications
            var mentions = Regex.Matches(text, @"@([A-Za-z0-9_\.\s]+)", RegexOptions.IgnoreCase);
            if (mentions.Count > 0)
            {
                var allUsers = await _context.Users.ToListAsync();
                var notifiedUserIds = new HashSet<string>();
                
                foreach (Match match in mentions)
                {
                    var mentionedName = match.Groups[1].Value.Trim();
                    // Match by FullName
                    var mentionedUser = allUsers.FirstOrDefault(u => 
                        string.Equals(u.FullName, mentionedName, StringComparison.OrdinalIgnoreCase));
                    
                    if (mentionedUser != null && mentionedUser.Id != userId && !notifiedUserIds.Contains(mentionedUser.Id))
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = mentionedUser.Id,
                            Title = "You were mentioned",
                            Message = $"{(comment.User?.FullName ?? "Someone")} mentioned you in a comment.",
                            Link = $"/TaskItems/Details/{taskId}",
                            Type = "Mention"
                        });
                        notifiedUserIds.Add(mentionedUser.Id);
                    }
                }
                await _context.SaveChangesAsync();
            }

            var html = $@"
                <div class='comment-card'>
                    <div class='comment-avatar'>
                        {(comment.User?.FullName?[0].ToString() ?? "?")}
                    </div>
                    <div class='comment-content'>
                        <div class='comment-header'>
                            <span class='comment-author'>{(comment.User?.FullName ?? "Unknown User")}</span>
                            <span class='comment-time'>{comment.CreatedAt.ToString("MMM dd, yyyy HH:mm")}</span>
                        </div>
                        <div class='comment-text'>{formattedText}</div>
                    </div>
                </div>";

            return Content(html, "text/html");
        }

        // GET: TaskItems/GetEligibleUsersForMention
        [HttpGet]
        public async Task<IActionResult> GetEligibleUsersForMention(int projectId)
        {
            // Simple implementation: return all valid users for now.
            // A more complex implementation would filter by project assignment.
            var users = await _context.Users
                .Select(u => new { key = u.FullName, value = u.FullName, email = u.Email })
                .ToListAsync();

            return Json(users);
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
