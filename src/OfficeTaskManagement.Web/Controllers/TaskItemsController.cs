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
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.WorkflowEngine;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class TaskItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IMediaService _mediaService;
        private readonly IResourceService _resourceService;
        private readonly IWorkflowEngineService _workflowEngine;
        private readonly KanbanGovernanceService _kanbanGovernance;

        public TaskItemsController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            IMediaService mediaService,
            IResourceService resourceService,
            IWorkflowEngineService workflowEngine,
            KanbanGovernanceService kanbanGovernance)
        {
            _context = context;
            _env = env;
            _mediaService = mediaService;
            _resourceService = resourceService;
            _workflowEngine = workflowEngine;
            _kanbanGovernance = kanbanGovernance;
        }

        // GET: TaskItems
        // showStages=true: reveal stage Activity sub-tasks ("My Stages" view)
        // projectId: optional filter to scope the board to a single project (drives Kanban columns)
        public async Task<IActionResult> Index(bool showBacklog = false, bool showStages = false, int? projectId = null, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc = null)
        {
            ViewBag.ShowBacklog = showBacklog;
            ViewBag.ShowStages = showStages;
            ViewBag.ProjectId = projectId;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.Project)
                .Include(t => t.Epic)
                .Include(t => t.Sprint)
                .Include(t => t.Feature)
                .Include(t => t.UserStory)
                .Include(t => t.Areas)
                .Include(t => t.SubTasks)
                .AsQueryable();

            var canSeeAll = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) || await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage);
            if (!canSeeAll)
            {
                var isLead = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
                if (isLead)
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

            // Optional project scope filter
            if (projectId.HasValue)
                query = query.Where(t => t.ProjectId == projectId.Value);

            if (showBacklog)
            {
                query = query.Where(t => t.Status == TaskStatus.New || t.Status == TaskStatus.Approved);
            }
            else
            {
                query = query.Where(t => t.Status >= TaskStatus.ToDo);
            }

            // By default hide stage Activity sub-tasks from the Kanban board.
            // Work Packages and standalone tasks are shown. Switch to "My Stages" view to see activities.
            if (!showStages)
                query = query.Where(t => t.WorkflowStageId == null);
            else
                query = query.Where(t => t.WorkflowStageId != null); // "My Stages" shows only activities

            // Compute dynamic Kanban columns from project's active workflow template
            ViewBag.KanbanColumns = showBacklog
                ? null  // backlog uses its own fixed columns (New, Approved)
                : await _kanbanGovernance.GetColumnsAsync(projectId);

            return View(await query.ToListAsync());
        }

        // GET: TaskItems/Details/5
        public async Task<IActionResult> Details(int? id, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
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
                .Include(t => t.Epic)
                .Include(t => t.Sprint)
                .Include(t => t.History).ThenInclude(h => h.ChangedBy)
                .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
                .Include(t => t.SubTasks)
                .Include(t => t.UserStory)
                .Include(t => t.Areas)
                .Include(t => t.AccountableUser)
                .Include(t => t.WorkflowStage)  // Required for DoD panel
                .AsQueryable();

            var canSeeAll = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) || await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage);
            if (!canSeeAll)
            {
                var isLead = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
                if (isLead)
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
        public IActionResult Create(int? projectId, bool isBacklog = false, Models.Enums.TaskStatus? status = null)
        {
            var taskItem = new TaskItem
            {
                IsBacklog = isBacklog
            };
            if (status.HasValue)
            {
                taskItem.Status = status.Value;
            }
            if (projectId.HasValue)
            {
                taskItem.ProjectId = projectId.Value;
            }

            var vm = new TaskItemViewModel
            {
                TaskItem = taskItem,
                UsersList = new SelectList(_context.Users, "Id", "Email"),
                ProjectsList = new SelectList(_context.Projects, "Id", "Name", projectId),
                EpicsList = new SelectList(new List<Epic>(), "Id", "Name"), // Initially empty
                SprintsList = new SelectList(projectId.HasValue
                    ? _context.Sprints.Where(s => s.ProjectId == projectId.Value)
                    : _context.Sprints, "Id", "Name"),
                FeaturesList = new SelectList(new List<Feature>(), "Id", "Name"), // Initially empty
                UserStoriesList = new SelectList(new List<UserStory>(), "Id", "Title"), // Initially empty
                AreasList = new MultiSelectList(_context.Areas, "Id", "Name"),
                ParentTasksList = new SelectList(projectId.HasValue
                    ? _context.Tasks.Where(t => t.ParentTaskId == null && t.ProjectId == projectId.Value)
                    : _context.Tasks.Where(t => t.ParentTaskId == null), "Id", "Title"),
                WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name"),
                AccountableUsersList = new SelectList(_context.Users, "Id", "Email")
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

                // ── P3-3: Circular parent prevention ────────────────────────────
                // For a brand-new task there is no self-id yet, so a cycle can only
                // exist if the proposed parent already has this task as an ancestor.
                // We guard by checking that the proposed parent isn't itself a
                // descendant of a new task (impossible for new — handled in Edit).
                // We still validate that the parent isn't a work-package stage child.
                if (vm.TaskItem.ParentTaskId.HasValue)
                {
                    var proposedParent = await _context.Tasks.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == vm.TaskItem.ParentTaskId.Value);
                    if (proposedParent?.IsWorkPackage == false && proposedParent.WorkflowStageId.HasValue)
                    {
                        ModelState.AddModelError("TaskItem.ParentTaskId",
                            "Cannot assign a stage activity as a parent task. Choose the Work Package instead.");
                    }
                }
                // ────────────────────────────────────────────────────────────────

                if (!ModelState.IsValid)
                {
                    vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
                    vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                    vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
                    vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                    vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
                    vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
                    vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                    vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                    vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                    vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                    return View(vm);
                }

                if (vm.TaskItem.EstimatedOptimisticHours.HasValue ||
                    vm.TaskItem.EstimatedMostLikelyHours.HasValue ||
                    vm.TaskItem.EstimatedPessimisticHours.HasValue)
                {
                    decimal o = vm.TaskItem.EstimatedOptimisticHours ?? 0;
                    decimal m = vm.TaskItem.EstimatedMostLikelyHours ?? 0;
                    decimal p = vm.TaskItem.EstimatedPessimisticHours ?? 0;
                    vm.TaskItem.PertEstimatedHours = _workflowEngine.CalculatePert(o, m, p);
                    vm.TaskItem.EstimatedHours = vm.TaskItem.PertEstimatedHours.Value;
                }

                _context.Add(vm.TaskItem);

                // Handle Areas
                if (vm.SelectedAreaIds != null)
                {
                    foreach (var areaId in vm.SelectedAreaIds)
                    {
                        var area = await _context.Areas.FindAsync(areaId);
                        if (area != null)
                        {
                            vm.TaskItem.Areas.Add(area);
                        }
                    }
                }
                
                // If this is a sub-task and its status is being actively worked (InProgress/Committed/Reviewed/Tested), ensure parent is also InProgress
                if (vm.TaskItem.ParentTaskId.HasValue &&
                    (vm.TaskItem.Status == TaskStatus.InProgress ||
                     vm.TaskItem.Status == TaskStatus.Committed  ||
                     vm.TaskItem.Status == TaskStatus.Reviewed   ||
                     vm.TaskItem.Status == TaskStatus.Tested))
                {
                    var parent = await _context.Tasks.FindAsync(vm.TaskItem.ParentTaskId);
                    if (parent != null &&
                        parent.Status != TaskStatus.InProgress &&
                        parent.Status != TaskStatus.Committed  &&
                        parent.Status != TaskStatus.Reviewed   &&
                        parent.Status != TaskStatus.Tested     &&
                        parent.Status != TaskStatus.Done)
                    {
                        parent.Status = TaskStatus.InProgress;
                        _context.Update(parent);
                    }
                }

                await _context.SaveChangesAsync();

                if (vm.SelectedWorkflowTemplateId.HasValue)
                {
                    await _workflowEngine.SpawnWorkflowSubTasksAsync(vm.TaskItem.Id, vm.SelectedWorkflowTemplateId.Value);
                }

                // Check for over-allocation
                if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId))
                {
                    bool overAllocated = await _resourceService.IsUserOverAllocatedAsync(
                        vm.TaskItem.AssigneeId,
                        vm.TaskItem.StartDate ?? DateTime.UtcNow,
                        vm.TaskItem.DueDate ?? (vm.TaskItem.StartDate ?? DateTime.UtcNow).AddDays(7)
                    );
                    if (overAllocated)
                    {
                        TempData["ResourceWarning"] = "Warning: The assigned user is over-allocated during the task's timeframe.";
                    }
                }

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
                                TaskItemId    = vm.TaskItem.Id,
                                FileName      = file.FileName,
                                FilePath      = filePath,
                                FileSizeBytes = file.Length,
                                ContentType   = file.ContentType,
                                UploadedById  = currentUserId,
                                UploadedAt    = DateTime.UtcNow
                            });
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                return RedirectToAction(nameof(Index));
            }
            
            vm.UsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AssigneeId);
            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
            vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
            vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
            vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
            vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
            vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
            return View(vm);
        }

        // GET: TaskItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var taskItem = await _context.Tasks
                .Include(t => t.Areas)
                .Include(t => t.WorkflowStage).ThenInclude(s => s!.Role)
                .Include(t => t.SubTasks).ThenInclude(s => s.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (taskItem == null)
            {
                return NotFound();
            }

            // Load WorkflowStage for gate-aware status rendering in the view
            if (taskItem.WorkflowStageId.HasValue)
            {
                await _context.Entry(taskItem).Reference(t => t.WorkflowStage).LoadAsync();
                if (taskItem.WorkflowStage != null && taskItem.WorkflowStage.RoleId != null)
                {
                    await _context.Entry(taskItem.WorkflowStage).Reference(s => s.Role).LoadAsync();
                }
            }

            IEnumerable<User> usersList = await _context.Users.ToListAsync();
            if (taskItem.WorkflowStage != null && !string.IsNullOrEmpty(taskItem.WorkflowStage.RoleId))
            {
                var roleId = taskItem.WorkflowStage.RoleId;
                usersList = await (from u in _context.Users
                                   join ur in _context.UserRoles on u.Id equals ur.UserId
                                   where ur.RoleId == roleId
                                   select u).ToListAsync();
            }
            
            int? currentTemplateId = taskItem.SubTasks?
                .FirstOrDefault(s => s.WorkflowStageId != null)?
                .WorkflowStage?.WorkflowTemplateId;

            var vm = new TaskItemViewModel
            {
                TaskItem = taskItem,
                SelectedWorkflowTemplateId = currentTemplateId,
                UsersList = new SelectList(usersList, "Id", "Email", taskItem.AssigneeId),
                ProjectsList = new SelectList(_context.Projects, "Id", "Name", taskItem.ProjectId),
                EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == taskItem.ProjectId), "Id", "Name", taskItem.EpicId),
                SprintsList = new SelectList(_context.Sprints, "Id", "Name", taskItem.SprintId),
                FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == taskItem.EpicId), "Id", "Name", taskItem.FeatureId),
                UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == taskItem.FeatureId), "Id", "Title", taskItem.UserStoryId),
                AreasList = new MultiSelectList(_context.Areas, "Id", "Name", taskItem.Areas.Select(a => a.Id)),
                SelectedAreaIds = taskItem.Areas.Select(a => a.Id).ToList(),
                ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != taskItem.Id), "Id", "Title", taskItem.ParentTaskId),
                WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name"),
                AccountableUsersList = new SelectList(_context.Users, "Id", "Email", taskItem.AccountableUserId)
            };
            // Note: SubTasks expressly included above in the initial query
            
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
            // Load WorkflowStage for gate-aware status rendering in the view
            if (taskItem.WorkflowStageId.HasValue)
                await _context.Entry(taskItem).Reference(t => t.WorkflowStage).LoadAsync();

            // Pass data to view so it doesn't need to inject DbContext
            ViewBag.AllUsers = await _context.Users.ToListAsync();

            return View(vm);
        }

        // POST: TaskItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TaskItemViewModel vm, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
        {
            if (id != vm.TaskItem.Id)
            {
                return NotFound();
            }

            var existingTask = await _context.Tasks
                .Include(t => t.Areas)
                .Include(t => t.WorkflowStage).ThenInclude(s => s!.Role)
                .Include(t => t.SubTasks).ThenInclude(s => s.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (existingTask == null) return NotFound();

            IEnumerable<User> usersList = await _context.Users.ToListAsync();
            if (existingTask.WorkflowStage != null && !string.IsNullOrEmpty(existingTask.WorkflowStage.RoleId))
            {
                var roleId = existingTask.WorkflowStage.RoleId;
                usersList = await (from u in _context.Users
                                   join ur in _context.UserRoles on u.Id equals ur.UserId
                                   where ur.RoleId == roleId
                                   select u).ToListAsync();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Ensure existing and incoming DateTimes are normalized to UTC
                    existingTask.CreatedAt = EnsureUtc(existingTask.CreatedAt);
                    vm.TaskItem.StartDate = EnsureUtc(vm.TaskItem.StartDate);
                    vm.TaskItem.DueDate = EnsureUtc(vm.TaskItem.DueDate);

                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var userRole = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);

                    // Validation: only approved item can be assigned to someone for 'todo'
                    if (vm.TaskItem.Status == TaskStatus.ToDo)
                    {
                        if (existingTask.Status != TaskStatus.Approved && existingTask.Status != TaskStatus.ToDo)
                        {
                            ModelState.AddModelError("", "Only Approved items can be moved to ToDo.");
                            // Re-populate lists and return view as done below for other errors
                        }
                        if (string.IsNullOrEmpty(vm.TaskItem.AssigneeId))
                        {
                            ModelState.AddModelError("", "Tasks in ToDo must have an assignee.");
                        }
                    }

                    // Validation: if stage has required dynamic role, assignee must be in that role (or higher)
                    if (existingTask.WorkflowStage != null && !string.IsNullOrEmpty(existingTask.WorkflowStage.RoleId))
                    {
                        if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId))
                        {
                            var inRole = await _context.UserRoles.AnyAsync(ur => ur.UserId == vm.TaskItem.AssigneeId && ur.RoleId == existingTask.WorkflowStage.RoleId);
                            if (!inRole)
                            {
                                // Also allow higher organizational roles (Super Admin, Admin, PM, Project Lead)
                                var assigneeRoleIds = await _context.UserRoles.Where(ur => ur.UserId == vm.TaskItem.AssigneeId).Select(ur => ur.RoleId).ToListAsync();
                                var assigneeRoles = await _context.Roles.Where(r => assigneeRoleIds.Contains(r.Id)).ToListAsync();
                                var minAssigneeLevel = assigneeRoles.Any() ? assigneeRoles.Min(r => r.HierarchyLevel) : int.MaxValue;
                                
                                var stageRole = existingTask.WorkflowStage.Role ?? await _context.Roles.FindAsync(existingTask.WorkflowStage.RoleId);
                                bool hasAuthorizedRole = stageRole != null && minAssigneeLevel <= stageRole.HierarchyLevel;
                                if (!hasAuthorizedRole)
                                {
                                    ModelState.AddModelError("TaskItem.AssigneeId", $"The selected user is not in the required dynamic role '{stageRole?.Name ?? "Required Role"}' (or a higher authority role) for this stage.");
                                }
                            }
                        }
                    }

                    if (!ModelState.IsValid)
                    {
                        vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                        vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                        vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
                        vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                        vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
                        vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
                        vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                        vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                        vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                        vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                        return View(vm);
                    }
                    
                    // ── P3-3: Circular parent prevention ────────────────────────
                    if (vm.TaskItem.ParentTaskId.HasValue)
                    {
                        // Self-assignment guard
                        if (vm.TaskItem.ParentTaskId.Value == id)
                        {
                            ModelState.AddModelError("TaskItem.ParentTaskId",
                                "A task cannot be its own parent.");
                        }
                        // Ancestor cycle guard — walk proposed parent's chain
                        else if (await WouldCreateCycleAsync(id, vm.TaskItem.ParentTaskId.Value))
                        {
                            ModelState.AddModelError("TaskItem.ParentTaskId",
                                "Circular parent detected: the chosen parent is already a descendant of this task. Choose a different parent.");
                        }
                        // Stage-activity guard — cannot parent to a stage sub-task
                        else
                        {
                            var proposedParent = await _context.Tasks.AsNoTracking()
                                .FirstOrDefaultAsync(t => t.Id == vm.TaskItem.ParentTaskId.Value);
                            if (proposedParent is { WorkflowStageId: not null, IsWorkPackage: false })
                            {
                                ModelState.AddModelError("TaskItem.ParentTaskId",
                                    "Cannot assign a stage activity as a parent. Choose the Work Package instead.");
                            }
                        }

                        if (!ModelState.IsValid)
                        {
                            vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
                            vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
                            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                            vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                            ViewBag.AllUsers = await _context.Users.ToListAsync();
                            return View(vm);
                        }
                    }
                    // ────────────────────────────────────────────────────────────

                    // GAP 9: Block manual status changes on Work Package Summary Tasks.
                    // Their status is derived exclusively via SyncParentStatusAsync.
                    if (existingTask.IsWorkPackage && vm.TaskItem.Status != existingTask.Status)
                    {
                        ModelState.AddModelError("", "The status of a Work Package is managed automatically by its stage gates. Use the Work Package pipeline view to advance stages.");
                        vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                        vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                        vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
                        vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                        vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
                        vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
                        vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                        vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                        vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                        vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                        return View(vm);
                    }

                    // Logic to enforce who can mark as done (non-WP standalone tasks)
                    if (!existingTask.IsWorkPackage && vm.TaskItem.Status == TaskStatus.Done)
                    {
                        if (!userRole && existingTask.CreatedById != userId)
                        {
                            ModelState.AddModelError("", "Only Project Lead, Manager, or Task Owner can mark as done.");
                            vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            vm.UserStoriesList = new SelectList(_context.UserStories, "Id", "Title", vm.TaskItem.UserStoryId);
                            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            return View(vm);
                        }

                        // Check Sub-tasks status: Cannot mark parent as Done if any sub-task is not Done
                        var hasOpenSubTasks = await _context.Tasks.AnyAsync(t => t.ParentTaskId == id && t.Status != TaskStatus.Done);
                        if (hasOpenSubTasks)
                        {
                            ModelState.AddModelError("", "Cannot mark this task as Done because it has open Sub-tasks. Please complete all Sub-tasks first.");
                            vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            vm.UserStoriesList = new SelectList(_context.UserStories, "Id", "Title", vm.TaskItem.UserStoryId);
                            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                            vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                            return View(vm);
                        }
                    }

                    int? currentTemplateId = existingTask.SubTasks?.FirstOrDefault(s => s.WorkflowStageId != null)?.WorkflowStage?.WorkflowTemplateId;

                    if (vm.SelectedWorkflowTemplateId != currentTemplateId)
                    {
                        var oldWfTasks = existingTask.SubTasks?.Where(s => s.WorkflowStageId != null).ToList() ?? new List<TaskItem>();
                        bool hasStarted = oldWfTasks.Any(s => s.Status != TaskStatus.New && s.Status != TaskStatus.ToDo);
                        
                        if (hasStarted)
                        {
                            ModelState.AddModelError("", "Cannot change or remove the pipeline because some stages have already been worked on.");
                            vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
                            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
                            vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
                            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
                            vm.FeaturesList = new SelectList(_context.Features, "Id", "Name", vm.TaskItem.FeatureId);
                            vm.UserStoriesList = new SelectList(_context.UserStories, "Id", "Title", vm.TaskItem.UserStoryId);
                            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
                            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
                            vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
                            vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
                            return View(vm);
                        }
                    }

                    // ── P3-4: Structured per-field audit history ────────────────
                    var actorRaci = userRole
                        ? OfficeTaskManagement.Models.Enums.RaciRole.Accountable
                        : OfficeTaskManagement.Models.Enums.RaciRole.Responsible;
                    var auditNow  = DateTime.UtcNow;

                    void AddAudit(string field, string? oldVal, string? newVal, string desc) =>
                        _context.TaskHistories.Add(new TaskHistory
                        {
                            TaskItemId        = existingTask.Id,
                            ChangedById       = userId,
                            FieldChanged      = field,
                            OldValue          = oldVal,
                            NewValue          = newVal,
                            RaciRoleAtTime    = actorRaci,
                            ChangeDescription = desc,
                            Timestamp         = auditNow
                        });

                    if (existingTask.Title != vm.TaskItem.Title)
                        AddAudit("Title", existingTask.Title, vm.TaskItem.Title, $"Title changed.");

                    if (existingTask.Status != vm.TaskItem.Status)
                    {
                        AddAudit("Status", existingTask.Status.ToString(), vm.TaskItem.Status.ToString(),
                            $"Status changed from {existingTask.Status} to {vm.TaskItem.Status}.");

                        // Set CompletedAt when transitioning to Done (standalone tasks)
                        if (vm.TaskItem.Status == TaskStatus.Done && !existingTask.IsWorkPackage)
                            existingTask.CompletedAt = auditNow;

                        // Notify creator on completion
                        if (vm.TaskItem.Status == TaskStatus.Done && existingTask.CreatedById != userId)
                            _context.Notifications.Add(new Notification
                            {
                                UserId  = existingTask.CreatedById,
                                Title   = "Task Completed",
                                Message = $"Task '{vm.TaskItem.Title}' was marked as Done.",
                                Link    = $"/TaskItems/Details/{existingTask.Id}",
                                Type    = "StatusUpdate"
                            });
                    }

                    if (existingTask.AssigneeId != vm.TaskItem.AssigneeId)
                    {
                        AddAudit("Assignee", existingTask.AssigneeId, vm.TaskItem.AssigneeId, "Assignee changed.");
                        if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId) && vm.TaskItem.AssigneeId != userId)
                            _context.Notifications.Add(new Notification
                            {
                                UserId  = vm.TaskItem.AssigneeId,
                                Title   = "Task Assignment Updated",
                                Message = $"You have been assigned to: {vm.TaskItem.Title}",
                                Link    = $"/TaskItems/Details/{existingTask.Id}",
                                Type    = "Assignment"
                            });
                    }

                    if (existingTask.AccountableUserId != vm.TaskItem.AccountableUserId)
                        AddAudit("AccountableUser", existingTask.AccountableUserId, vm.TaskItem.AccountableUserId,
                            "Accountable (A) party changed.");

                    if (existingTask.Priority != vm.TaskItem.Priority)
                        AddAudit("Priority", existingTask.Priority.ToString(), vm.TaskItem.Priority.ToString(),
                            $"Priority changed from {existingTask.Priority} to {vm.TaskItem.Priority}.");

                    if (existingTask.StartDate != vm.TaskItem.StartDate)
                        AddAudit("StartDate",
                            existingTask.StartDate?.ToString("o"), vm.TaskItem.StartDate?.ToString("o"),
                            "Start Date changed.");

                    if (existingTask.DueDate != vm.TaskItem.DueDate)
                        AddAudit("DueDate",
                            existingTask.DueDate?.ToString("o"), vm.TaskItem.DueDate?.ToString("o"),
                            "Due Date changed.");

                    if (existingTask.EstimatedHours != vm.TaskItem.EstimatedHours)
                        AddAudit("EstimatedHours",
                            existingTask.EstimatedHours.ToString("F2"),
                            vm.TaskItem.EstimatedHours.ToString("F2"),
                            "Estimated Hours changed.");

                    if (existingTask.ActualHours != vm.TaskItem.ActualHours)
                        AddAudit("ActualHours",
                            existingTask.ActualHours?.ToString("F2"),
                            vm.TaskItem.ActualHours?.ToString("F2"),
                            "Actual Hours updated.");

                    if (existingTask.ParentTaskId != vm.TaskItem.ParentTaskId)
                        AddAudit("ParentTask",
                            existingTask.ParentTaskId?.ToString(), vm.TaskItem.ParentTaskId?.ToString(),
                            "Parent task reassigned.");

                    if (existingTask.SprintId != vm.TaskItem.SprintId)
                        AddAudit("Sprint", existingTask.SprintId?.ToString(), vm.TaskItem.SprintId?.ToString(),
                            "Sprint changed.");

                    if (existingTask.ProjectId != vm.TaskItem.ProjectId)
                        AddAudit("Project", existingTask.ProjectId?.ToString(), vm.TaskItem.ProjectId?.ToString(),
                            "Project changed.");
                    // ────────────────────────────────────────────────────────────

                    existingTask.Title = vm.TaskItem.Title;
                    existingTask.Description = vm.TaskItem.Description;
                    existingTask.Status = vm.TaskItem.Status;
                    existingTask.EstimatedHours = vm.TaskItem.EstimatedHours;
                    existingTask.StartDate = vm.TaskItem.StartDate;
                    existingTask.DueDate = vm.TaskItem.DueDate;
                    existingTask.ProjectId = vm.TaskItem.ProjectId;
                    existingTask.EpicId = vm.TaskItem.EpicId;
                    existingTask.SprintId = vm.TaskItem.SprintId;
                    existingTask.FeatureId = vm.TaskItem.FeatureId;
                    existingTask.UserStoryId = vm.TaskItem.UserStoryId;
                    existingTask.AssigneeId = vm.TaskItem.AssigneeId;
                    existingTask.ParentTaskId = vm.TaskItem.ParentTaskId;
                    existingTask.Type = vm.TaskItem.Type;

                    existingTask.AccountableUserId = vm.TaskItem.AccountableUserId;
                    existingTask.ActualHours = vm.TaskItem.ActualHours;

                    if (existingTask.EstimatedOptimisticHours != vm.TaskItem.EstimatedOptimisticHours ||
                        existingTask.EstimatedMostLikelyHours != vm.TaskItem.EstimatedMostLikelyHours ||
                        existingTask.EstimatedPessimisticHours != vm.TaskItem.EstimatedPessimisticHours)
                    {
                        existingTask.EstimatedOptimisticHours = vm.TaskItem.EstimatedOptimisticHours;
                        existingTask.EstimatedMostLikelyHours = vm.TaskItem.EstimatedMostLikelyHours;
                        existingTask.EstimatedPessimisticHours = vm.TaskItem.EstimatedPessimisticHours;

                        if (existingTask.EstimatedOptimisticHours.HasValue ||
                            existingTask.EstimatedMostLikelyHours.HasValue ||
                            existingTask.EstimatedPessimisticHours.HasValue)
                        {
                            decimal o = existingTask.EstimatedOptimisticHours ?? 0;
                            decimal m = existingTask.EstimatedMostLikelyHours ?? 0;
                            decimal p = existingTask.EstimatedPessimisticHours ?? 0;
                            existingTask.PertEstimatedHours = _workflowEngine.CalculatePert(o, m, p);
                            existingTask.EstimatedHours = existingTask.PertEstimatedHours.Value;
                        }
                    }

                    // Update Areas
                    existingTask.Areas.Clear();
                    if (vm.SelectedAreaIds != null)
                    {
                        foreach (var areaId in vm.SelectedAreaIds)
                        {
                            var area = await _context.Areas.FindAsync(areaId);
                            if (area != null)
                            {
                                existingTask.Areas.Add(area);
                            }
                        }
                    }

                    _context.Update(existingTask);
                    
                    // If a sub-task is moving into a worked state, parent must be at least InProgress
                    if (existingTask.ParentTaskId.HasValue &&
                        (existingTask.Status == TaskStatus.InProgress ||
                         existingTask.Status == TaskStatus.Committed  ||
                         existingTask.Status == TaskStatus.Reviewed   ||
                         existingTask.Status == TaskStatus.Tested))
                    {
                        var parent = await _context.Tasks.FindAsync(existingTask.ParentTaskId);
                        if (parent != null &&
                            parent.Status != TaskStatus.InProgress &&
                            parent.Status != TaskStatus.Committed  &&
                            parent.Status != TaskStatus.Reviewed   &&
                            parent.Status != TaskStatus.Tested     &&
                            parent.Status != TaskStatus.Done)
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
                                    TaskItemId    = existingTask.Id,
                                    FileName      = file.FileName,
                                    FilePath      = filePath,
                                    FileSizeBytes = file.Length,
                                    ContentType   = file.ContentType,
                                    UploadedById  = userId,
                                    UploadedAt    = DateTime.UtcNow
                                });
                            }
                        }
                        
                        _context.TaskHistories.Add(new TaskHistory
                        {
                            TaskItemId = vm.TaskItem.Id,
                            ChangedById = userId,
                            ChangeDescription = "Attachments added."
                        });
                    }

                    await _context.SaveChangesAsync();

                    if (vm.SelectedWorkflowTemplateId != currentTemplateId)
                    {
                        var oldWfTasks = existingTask.SubTasks?.Where(s => s.WorkflowStageId != null).ToList() ?? new List<TaskItem>();
                        if (oldWfTasks.Any())
                        {
                            _context.Tasks.RemoveRange(oldWfTasks);
                        }

                        if (vm.SelectedWorkflowTemplateId.HasValue)
                        {
                            await _workflowEngine.SpawnWorkflowSubTasksAsync(existingTask.Id, vm.SelectedWorkflowTemplateId.Value);
                            _context.TaskHistories.Add(new TaskHistory
                            {
                                TaskItemId = existingTask.Id,
                                ChangedById = userId,
                                ChangeDescription = currentTemplateId.HasValue ? "Workflow pipeline changed to new template." : "Workflow pipeline applied."
                            });
                        }
                        else if (currentTemplateId.HasValue)
                        {
                            _context.TaskHistories.Add(new TaskHistory
                            {
                                TaskItemId = existingTask.Id,
                                ChangedById = userId,
                                ChangeDescription = "Workflow pipeline removed."
                            });
                        }
                        
                        await _context.SaveChangesAsync();
                    }

                    // GAP 2: After editing a stage sub-task, roll up the parent WP status
                    if (existingTask.ParentTaskId.HasValue && existingTask.WorkflowStageId.HasValue)
                    {
                        await _workflowEngine.SyncParentStatusAsync(existingTask.ParentTaskId.Value, userId);
                    }

                    // Check for over-allocation
                    if (!string.IsNullOrEmpty(vm.TaskItem.AssigneeId))
                    {
                        bool overAllocated = await _resourceService.IsUserOverAllocatedAsync(
                            vm.TaskItem.AssigneeId,
                            vm.TaskItem.StartDate ?? DateTime.UtcNow,
                            vm.TaskItem.DueDate ?? (vm.TaskItem.StartDate ?? DateTime.UtcNow).AddDays(7)
                        );
                        if (overAllocated)
                        {
                            TempData["ResourceWarning"] = "Warning: The assigned user is over-allocated during the task's timeframe.";
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // Gate violation from StageGateService — show as friendly form error
                    ModelState.AddModelError("", $"Stage Gate: {ex.Message}");
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TaskItemExists(vm.TaskItem.Id))
                        return NotFound();
                    throw;
                }

                if (ModelState.IsValid)
                    return RedirectToAction(nameof(Index));
            }

            // Re-populate all lists for error re-render
            vm.UsersList = new SelectList(usersList, "Id", "Email", vm.TaskItem.AssigneeId);
            vm.ProjectsList = new SelectList(_context.Projects, "Id", "Name", vm.TaskItem.ProjectId);
            vm.EpicsList = new SelectList(_context.Epics.Where(e => e.ProjectId == vm.TaskItem.ProjectId), "Id", "Name", vm.TaskItem.EpicId);
            vm.SprintsList = new SelectList(_context.Sprints, "Id", "Name", vm.TaskItem.SprintId);
            vm.FeaturesList = new SelectList(_context.Features.Where(f => f.EpicId == vm.TaskItem.EpicId), "Id", "Name", vm.TaskItem.FeatureId);
            vm.UserStoriesList = new SelectList(_context.UserStories.Where(u => u.FeatureId == vm.TaskItem.FeatureId), "Id", "Title", vm.TaskItem.UserStoryId);
            vm.AreasList = new MultiSelectList(_context.Areas, "Id", "Name", vm.SelectedAreaIds);
            vm.ParentTasksList = new SelectList(_context.Tasks.Where(t => t.ParentTaskId == null && t.Id != vm.TaskItem.Id), "Id", "Title", vm.TaskItem.ParentTaskId);
            vm.WorkflowTemplatesList = new SelectList(_context.WorkflowTemplates.Where(t => t.IsActive), "Id", "Name");
            vm.AccountableUsersList = new SelectList(_context.Users, "Id", "Email", vm.TaskItem.AccountableUserId);
            ViewBag.AllUsers = await _context.Users.ToListAsync();
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

            // Cascade warning counts
            var subTaskCount = await _context.Tasks.CountAsync(t => t.ParentTaskId == id);
            var stageActivityCount = await _context.Tasks
                .CountAsync(t => t.ParentTaskId == id && t.WorkflowStageId != null);
            var commentCount = await _context.TaskComments.CountAsync(c => c.TaskId == id);
            var historyCount = await _context.TaskHistories.CountAsync(h => h.TaskItemId == id);

            ViewBag.SubTaskCount = subTaskCount;
            ViewBag.StageActivityCount = stageActivityCount;
            ViewBag.CommentCount = commentCount;
            ViewBag.HistoryCount = historyCount;

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
        public async Task<IActionResult> DeleteAttachment(int id, [FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var taskId = attachment.TaskItemId;
            if (taskId == null) return BadRequest("Attachment is not linked to a task.");
            
            // Access check: only the uploader, or Manager/ProjectLead can delete it
            var isLeadOrAdmin = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);
            if (attachment.UploadedById != userId && !isLeadOrAdmin)
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);

            _context.Attachments.Remove(attachment);
            
            _context.TaskHistories.Add(new TaskHistory
            {
                TaskItemId = taskId.Value,
                ChangedById = userId,
                ChangeDescription = $"Attachment '{attachment.FileName}' deleted.",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = taskId.Value });
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
                UserId = userId!,
                CommentText = text,
                CreatedAt = DateTime.UtcNow
            };

            _context.TaskComments.Add(comment);
            await _context.SaveChangesAsync();

            // Load user to return in partial view
            await _context.Entry(comment).Reference(c => c.User).LoadAsync();

            var formattedText = comment.CommentText.Replace("\n", "<br/>");

            // Extract Mentions using substring match on FullName
            var allUsers = await _context.Users.ToListAsync();
            var notifiedUserIds = new HashSet<string>();
            
            foreach (var u in allUsers)
            {
                if (!string.IsNullOrEmpty(u.FullName) && text.Contains("@" + u.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    if (u.Id != userId && !notifiedUserIds.Contains(u.Id))
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = u.Id,
                            Title = "You were mentioned",
                            Message = $"{(comment.User?.FullName ?? "Someone")} mentioned you in a comment.",
                            Link = $"/TaskItems/Details/{taskId}",
                            Type = "Mention"
                        });
                        notifiedUserIds.Add(u.Id);
                    }
                    
                    var pattern = Regex.Escape("@" + u.FullName);
                    formattedText = Regex.Replace(formattedText, pattern, $"<span class='mention-badge'>@{u.FullName}</span>", RegexOptions.IgnoreCase);
                }
            }
            if (notifiedUserIds.Any()) 
            {
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

        public async Task<IActionResult> GetEligibleUsersForMention(int projectId)
        {
            // Simple implementation: return all valid users for now.
            // A more complex implementation would filter by project assignment.
            var users = await _context.Users
                .Select(u => new { key = u.FullName, value = u.FullName, email = u.Email })
                .ToListAsync();

            return Json(users);
        }

        [HttpGet]
        public async Task<IActionResult> GetEpics(int projectId)
        {
            var epics = await _context.Epics
                .Where(e => e.ProjectId == projectId)
                .Select(e => new { id = e.Id, name = e.Name })
                .ToListAsync();
            return Json(epics);
        }

        [HttpGet]
        public async Task<IActionResult> GetFeatures(int epicId)
        {
            var features = await _context.Features
                .Where(f => f.EpicId == epicId)
                .Select(f => new { id = f.Id, name = f.Name })
                .ToListAsync();
            return Json(features);
        }

        [HttpGet]
        public async Task<IActionResult> GetUserStories(int featureId)
        {
            var stories = await _context.UserStories
                .Where(u => u.FeatureId == featureId)
                .Select(u => new { id = u.Id, name = u.Title })
                .ToListAsync();
            return Json(stories);
        }

        // POST: TaskItems/UpdateStatus (AJAX — cookie auth for Kanban drag-and-drop)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, int statusId)
        {
            var task = await _context.Tasks
                .Include(t => t.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var newStatus = (Models.Enums.TaskStatus)statusId;

            // Block Work Package summary tasks
            if (task.IsWorkPackage)
                return Json(new { success = false, error = "Work Package status is managed by stage gates." });

            // Block paused tasks
            if (task.IsPaused)
                return Json(new { success = false, error = "This task is paused." });

            // ToDo requires assignee
            if (newStatus == TaskStatus.ToDo && string.IsNullOrEmpty(task.AssigneeId))
                return Json(new { success = false, error = "Tasks in ToDo must have an assignee." });

            var oldStatus = task.Status;
            task.Status = newStatus;

            if (newStatus == TaskStatus.New || newStatus == TaskStatus.Approved)
            {
                task.IsBacklog = true;
            }

            _context.TaskHistories.Add(new TaskHistory
            {
                TaskItemId = task.Id,
                ChangedById = userId,
                FieldChanged = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                ChangeDescription = $"Status changed from {oldStatus} to {newStatus} via Kanban board.",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Sync parent if stage sub-task
            if (task.ParentTaskId.HasValue && task.WorkflowStageId.HasValue)
                await _workflowEngine.SyncParentStatusAsync(task.ParentTaskId.Value, userId);
            else if (task.ParentTaskId.HasValue && newStatus >= TaskStatus.InProgress)
            {
                var parent = await _context.Tasks.FindAsync(task.ParentTaskId);
                if (parent != null && parent.Status < TaskStatus.InProgress && parent.Status != TaskStatus.Done)
                {
                    parent.Status = TaskStatus.InProgress;
                    await _context.SaveChangesAsync();
                }
            }

            return Json(new { success = true, status = newStatus.ToString() });
        }

        [HttpGet]
        public async Task<IActionResult> GetSprints(int projectId)
        {
            var sprints = await _context.Sprints
                .Where(s => s.ProjectId == projectId)
                .Select(s => new { id = s.Id, name = s.Name })
                .ToListAsync();
            return Json(sprints);
        }

        private bool TaskItemExists(int id)
        {
            return _context.Tasks.Any(e => e.Id == id);
        }

        /// <summary>
        /// P3-3: Detects if setting <paramref name="proposedParentId"/> as the parent of
        /// <paramref name="childId"/> would create a circular ancestor chain.
        /// Walks the proposed parent's ancestor chain; if it encounters <paramref name="childId"/>
        /// the assignment would create a cycle.
        /// </summary>
        private async Task<bool> WouldCreateCycleAsync(int childId, int proposedParentId)
        {
            const int maxDepth = 50; // guard against degenerate chains
            var current = proposedParentId;
            var visited = new HashSet<int>();

            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current == childId) return true; // cycle detected
                if (!visited.Add(current)) return false; // already seen, no cycle

                var task = await _context.Tasks
                    .AsNoTracking()
                    .Where(t => t.Id == current)
                    .Select(t => new { t.ParentTaskId })
                    .FirstOrDefaultAsync();

                if (task?.ParentTaskId == null) return false; // reached root
                current = task.ParentTaskId.Value;
            }

            return false; // depth limit hit — assume no cycle rather than false-positive
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
