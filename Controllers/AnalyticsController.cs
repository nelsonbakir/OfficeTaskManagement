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
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.ViewModels.Analytics;

using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Project Lead,Project Coordinator,Employee")]
    public class AnalyticsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────────────────────────────
        // STRATEGIC HUB — Manager/C-Suite Command Centre
        // ─────────────────────────────────────────────────────────────────────
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> StrategicHub()
        {
            var vm = await GetPortfolioIntelligence();

            vm.AllUsers = (await _context.Users.OrderBy(u => u.FullName).ToListAsync())
                .Select(u => new SelectListItem(u.FullName ?? u.Email, u.Id)).ToList();

            vm.AllProjects = (await _context.Projects.OrderBy(p => p.Name).ToListAsync())
                .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();

            vm.AllActiveSprints = (await _context.Sprints
                .Where(s => s.IsActive)
                .Include(s => s.Project)
                .OrderBy(s => s.Name)
                .ToListAsync())
                .Select(s => new SelectListItem($"{s.Name} ({s.Project?.Name})", s.Id.ToString())).ToList();

            vm.ActiveTasksForReassign = (await _context.Tasks
                .Where(t => t.Status != TaskStatus.Done)
                .Include(t => t.Project)
                .OrderBy(t => t.Title)
                .ToListAsync())
                .Select(t => new SelectListItem($"{t.Title} [{t.Project?.Name ?? "No Project"}]", t.Id.ToString())).ToList();

            return View(vm);
        }

        private async Task<StrategicHubViewModel> GetPortfolioIntelligence()
        {
            var vm = new StrategicHubViewModel();
            var now = DateTime.UtcNow;
            var weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
            var lastWeekStart = weekStart.AddDays(-7);

            // ── Fetch core data ──────────────────────────────────────────────
            var allUsers = await _context.Users.ToListAsync();
            var allProjects = await _context.Projects
                .Include(p => p.Sprints)
                .Include(p => p.PortfolioDecisions)
                .ToListAsync();

            var allTasks = await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.PausedBy)
                .Include(t => t.History)
                .ToListAsync();

            var activeSprints = await _context.Sprints
                .Where(s => s.IsActive)
                .Include(s => s.Project)
                .ToListAsync();

            // ── Org Capacity ─────────────────────────────────────────────────
            const decimal hoursPerPersonPerSprint = 40m;
            var committedHours = allTasks
                .Where(t => t.Status != TaskStatus.Done && !t.IsPaused)
                .Sum(t => t.EstimatedHours);
            var availableHours = allUsers.Count * hoursPerPersonPerSprint;

            var utilPct = availableHours > 0 ? committedHours / availableHours * 100 : 0;
            vm.CapacitySnapshot = new OrgCapacity
            {
                CommittedHours = committedHours,
                AvailableHours = availableHours,
                TeamSize = allUsers.Count,
                ActiveProjects = allProjects.Count(p => p.StrategicStatus == ProjectStrategicStatus.Active),
                Status = utilPct < 50 ? CapacityStatus.Free
                    : utilPct < 80 ? CapacityStatus.Balanced
                    : utilPct <= 100 ? CapacityStatus.AtRisk
                    : CapacityStatus.Overloaded
            };

            var atRiskProjectCount = allProjects.Count(p => p.StrategicStatus == ProjectStrategicStatus.Active
                && allTasks.Where(t => t.ProjectId == p.Id).Any(t => t.DueDate < now && t.Status != TaskStatus.Done));

            vm.CanStartNewProject = utilPct < 70 && atRiskProjectCount == 0;
            vm.NewProjectRecommendation = vm.CanStartNewProject
                ? $"✅ Yes — team is at {utilPct:F0}% capacity with no at-risk projects. Good time to onboard a new project."
                : utilPct >= 70 && atRiskProjectCount == 0
                    ? $"⚠️ Cautious — team is at {utilPct:F0}% capacity. Resolve load before adding new projects."
                    : $"🔴 No — {atRiskProjectCount} project(s) are at risk and team is at {utilPct:F0}% capacity. Stabilise first.";

            // ── Project Health Cards ────────────────────────────────────────
            foreach (var project in allProjects)
            {
                var pTasks = allTasks.Where(t => t.ProjectId == project.Id).ToList();
                if (!pTasks.Any()) { /* still show empty projects */ }

                var completed = pTasks.Count(t => t.Status == TaskStatus.Done);
                var overdue = pTasks.Count(t => t.DueDate < now && t.Status != TaskStatus.Done);
                var totalPTasks = pTasks.Count;

                var completionPct = totalPTasks > 0 ? (decimal)completed / totalPTasks * 100 : 0;
                var onTimePct = totalPTasks > 0 ? (decimal)(totalPTasks - overdue) / totalPTasks * 100 : 100;

                var activeMembers = pTasks
                    .Where(t => t.Status != TaskStatus.Done && t.AssigneeId != null && t.CreatedAt >= weekStart)
                    .Select(t => t.AssigneeId)
                    .Distinct().Count();
                var teamSize = pTasks.Select(t => t.AssigneeId).Distinct().Count(id => id != null);
                var engagementPct = teamSize > 0 ? (decimal)activeMembers / teamSize * 100 : 0;

                var healthScore = (completionPct * 0.4m) + (onTimePct * 0.4m) + (engagementPct * 0.2m);

                vm.ProjectHealthCards.Add(new ProjectHealthCard
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    StrategicStatus = project.StrategicStatus,
                    CompletionPercent = Math.Round(completionPct, 1),
                    OnTimeRate = Math.Round(onTimePct, 1),
                    TeamEngagement = Math.Round(engagementPct, 1),
                    TotalTasks = totalPTasks,
                    CompletedTasks = completed,
                    OverdueTasks = overdue,
                    TeamSize = teamSize,
                    HealthScore = Math.Round(healthScore, 1),
                    StrategicStatusReason = project.StrategicStatusReason,
                    StrategicStatusChangedAt = project.StrategicStatusChangedAt
                });
            }

            // ── Individual Engagement Scorecards ────────────────────────────
            foreach (var user in allUsers)
            {
                var userTasks = allTasks.Where(t => t.AssigneeId == user.Id).ToList();
                var closedThisWeek = userTasks.Count(t =>
                    t.Status == TaskStatus.Done &&
                    t.History.Any(h => h.Timestamp >= weekStart));
                var closedLastWeek = userTasks.Count(t =>
                    t.Status == TaskStatus.Done &&
                    t.History.Any(h => h.Timestamp >= lastWeekStart && h.Timestamp < weekStart));

                var overdue = userTasks.Count(t => t.DueDate < now && t.Status != TaskStatus.Done);
                var committed = userTasks.Where(t => t.Status != TaskStatus.Done).Sum(t => t.EstimatedHours);

                var level = committed == 0 ? "Idle"
                    : committed > hoursPerPersonPerSprint ? "Overloaded"
                    : "Engaged";

                vm.MemberScorecards.Add(new EngagementScorecard
                {
                    UserId = user.Id,
                    UserName = user.FullName ?? user.Email ?? "Unknown",
                    TasksClosedThisWeek = closedThisWeek,
                    TasksClosedLastWeek = closedLastWeek,
                    OverdueTasks = overdue,
                    CommittedHours = committed,
                    EngagementLevel = level
                });
            }

            // ── Suggested Reallocations ─────────────────────────────────────
            var idleMembers = vm.MemberScorecards.Where(m => m.EngagementLevel == "Idle").ToList();
            var atRiskProjects = vm.ProjectHealthCards
                .Where(p => p.RagStatus == "Red" && p.StrategicStatus == ProjectStrategicStatus.Active)
                .OrderBy(p => p.HealthScore)
                .ToList();

            foreach (var idle in idleMembers)
            {
                var target = atRiskProjects.FirstOrDefault();
                if (target != null)
                {
                    vm.SuggestedReallocations.Add(new SuggestedReallocation
                    {
                        UserId = idle.UserId,
                        UserName = idle.UserName,
                        CurrentEngagementLevel = idle.EngagementLevel,
                        SuggestedProjectId = target.ProjectId,
                        SuggestedProjectName = target.ProjectName,
                        Reason = $"{idle.UserName} has no committed work. {target.ProjectName} is at {target.HealthScore:F0}/100 health."
                    });
                }
            }

            // ── Paused Tasks ────────────────────────────────────────────────
            vm.PausedTasks = allTasks
                .Where(t => t.IsPaused)
                .Select(t => new PausedTaskCard
                {
                    TaskId = t.Id,
                    TaskTitle = t.Title,
                    AssigneeName = t.Assignee?.FullName ?? "Unassigned",
                    ProjectName = t.Project?.Name ?? "No Project",
                    PauseReason = t.PauseReason ?? "",
                    PausedAt = t.PausedAt,
                    PausedByName = t.PausedBy?.FullName ?? "Unknown"
                })
                .ToList();

            // ── Recent Decisions (audit trail) ──────────────────────────────
            vm.RecentDecisions = await _context.PortfolioDecisions
                .Include(d => d.MadeBy)
                .Include(d => d.Project)
                .OrderByDescending(d => d.MadeAt)
                .Take(20)
                .ToListAsync();

            return vm;
        }


        public async Task<IActionResult> Index(string? assigneeId, int? projectId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userRoles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
            var isManager = userRoles.Contains("Manager");
            var isProjectLead = userRoles.Contains("Project Lead");
            var isCoordinator = userRoles.Contains("Project Coordinator");
            var isEmployee = userRoles.Contains("Employee");

            var vm = new DashboardViewModel
            {
                SelectedAssigneeId = assigneeId,
                SelectedProjectId = projectId,
                UserRole = userRoles.FirstOrDefault() ?? "Employee"
            };

            // MANAGER DASHBOARD
            if (isManager)
            {
                vm.ManagerMetrics = await GetManagerMetrics(assigneeId, projectId);
                var assigneesList = await _context.Users.ToListAsync();
                var projectsList = await _context.Projects.ToListAsync();
                vm.Assignees = new SelectList(assigneesList, "Id", "Email", assigneeId);
                vm.Projects = new SelectList(projectsList, "Id", "Name", projectId);
            }
            // PROJECT LEAD DASHBOARD
            else if (isProjectLead)
            {
                var userProjects = await _context.Projects
                    .Where(p => p.CreatedById == userId)
                    .Select(p => p.Id)
                    .ToListAsync();

                vm.ProjectLeadMetrics = await GetProjectLeadMetrics(userId, userProjects);

                var assigneesList = await _context.Users.ToListAsync();
                var projectsList = await _context.Projects.Where(p => p.CreatedById == userId).ToListAsync();
                vm.Assignees = new SelectList(assigneesList, "Id", "Email", assigneeId);
                vm.Projects = new SelectList(projectsList, "Id", "Name", projectId);
            }
            // COORDINATOR DASHBOARD
            else if (isCoordinator)
            {
                vm.CoordinatorMetrics = await GetCoordinatorMetrics();
            }
            // EMPLOYEE DASHBOARD
            else if (isEmployee)
            {
                var user = await _context.Users.FindAsync(userId);
                vm.EmployeeMetrics = await GetEmployeeMetrics(userId, user?.FullName ?? "Employee");
            }

            return View("Dashboard", vm);
        }

        public async Task<IActionResult> Reports(string? assigneeId, int? projectId)
        {
            var vm = new DashboardViewModel
            {
                SelectedAssigneeId = assigneeId,
                SelectedProjectId = projectId
            };

            var assigneesList = await _context.Users.ToListAsync();
            var projectsList = await _context.Projects.ToListAsync();
            vm.Assignees = new SelectList(assigneesList, "Id", "Email", assigneeId);
            vm.Projects = new SelectList(projectsList, "Id", "Name", projectId);

            var tasksQuery = _context.Tasks.Include(t => t.Assignee).Include(t => t.Sprint).AsQueryable();
            if (!string.IsNullOrEmpty(assigneeId))
                tasksQuery = tasksQuery.Where(t => t.AssigneeId == assigneeId);
            if (projectId.HasValue)
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);

            var tasks = await tasksQuery.ToListAsync();
            var sprintsQuery = _context.Sprints.Include(s => s.Tasks).AsQueryable();
            if (projectId.HasValue)
            {
                sprintsQuery = sprintsQuery.Where(s => s.ProjectId == projectId.Value);
            }
            var sprints = await sprintsQuery.ToListAsync();

            vm.Engagements = tasks.Select(t => new DailyEngagement
            {
                AssigneeName = t.Assignee?.FullName ?? "Unassigned",
                TaskTitle = t.Title,
                Date = t.DueDate ?? DateTime.Today,
                Hours = t.EstimatedHours,
                IsToDo = t.Status == TaskStatus.New || t.Status == TaskStatus.Approved || t.Status == TaskStatus.ToDo
            }).ToList();

            var currentUtc = DateTime.UtcNow;

            // Definition of completed sprints: Inactive OR EndDate passed OR (Has Tasks AND All Tasks Done)
            var completedSprints = sprints.Where(s => 
                !s.IsActive || 
                s.EndDate < currentUtc || 
                (s.Tasks.Any() && s.Tasks.All(t => t.Status == TaskStatus.Done))).ToList();

            vm.Velocities = completedSprints.Select(s => new SprintVelocity
           {
               SprintName = s.Name,
               CompletedHours = s.Tasks.Where(t => t.Status == TaskStatus.Done).Sum(t => t.EstimatedHours)
           }).ToList();

            var activeSprints = sprints.Where(s => !completedSprints.Contains(s)).ToList();

            foreach (var sprint in activeSprints)
            {
                var sprintTasks = sprint.Tasks.AsEnumerable();
                
                // If an Assignee filter is active, only calculate burndown for their tasks
                if (!string.IsNullOrEmpty(assigneeId))
                {
                    sprintTasks = sprintTasks.Where(t => t.AssigneeId == assigneeId);
                }

                var totalHours = sprintTasks.Sum(t => t.EstimatedHours);
                var days = (sprint.EndDate - sprint.StartDate).Days;
                if (days > 0 && totalHours > 0)
                {
                    for (int i = 0; i <= days; i++)
                    {
                        var date = sprint.StartDate.AddDays(i);
                        var idealRemaining = totalHours - (totalHours / days) * i;
                        vm.Burndowns.Add(new SprintBurndown
                        {
                            SprintName = sprint.Name,
                            Date = date,
                            RemainingHours = idealRemaining < 0 ? 0 : idealRemaining
                        });
                    }
                }
            }

            return View("Index", vm);
        }

        public async Task<IActionResult> AIInsights()
        {
            var userRoles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
            var projectsList = await _context.Projects.ToListAsync();
            
            ViewBag.Projects = new SelectList(projectsList, "Id", "Name");
            ViewBag.UserRole = userRoles.FirstOrDefault() ?? "Employee";

            return View();
        }

        private async Task<ManagerDashboard> GetManagerMetrics(string? assigneeId, int? projectId)
        {
            var metrics = new ManagerDashboard();

            // Project metrics
            var projects = await _context.Projects.Include(p => p.Sprints).ToListAsync();
            metrics.TotalProjects = projects.Count;
            metrics.ActiveProjects = projects.Count(p => p.Sprints.Any(s => s.IsActive));

            // Team metrics
            var allUsers = await _context.Users.ToListAsync();
            metrics.TotalTeamMembers = allUsers.Count;

            // Task metrics
            var allTasks = await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.Project)
                .ToListAsync();

            var completedTasks = allTasks.Count(t => t.Status == TaskStatus.Done);
            metrics.OverallTaskCompletion = allTasks.Any() ? (int)((completedTasks * 100) / allTasks.Count) : 0;
            metrics.OverdueTasks = allTasks.Count(t => t.DueDate.HasValue && t.DueDate < DateTime.Now && t.Status != TaskStatus.Done);
            metrics.AtRiskTasks = allTasks.Count(t => (t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested) && t.DueDate.HasValue && t.DueDate < DateTime.Now.AddDays(3));

            // Project metrics
            foreach (var project in projects)
            {
                var projectTasks = allTasks.Where(t => t.ProjectId == project.Id).ToList();
                metrics.ProjectMetrics.Add(new ProjectMetric
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    TotalTasks = projectTasks.Count,
                    CompletedTasks = projectTasks.Count(t => t.Status == TaskStatus.Done),
                    Completion = projectTasks.Any() ? (decimal)projectTasks.Count(t => t.Status == TaskStatus.Done) / projectTasks.Count * 100 : 0,
                    TeamSize = projectTasks.Select(t => t.AssigneeId).Distinct().Count(),
                    Status = DetermineProjectStatus(projectTasks)
                });
            }

            // Employee metrics
            foreach (var employee in allUsers)
            {
                var employeeTasks = allTasks.Where(t => t.AssigneeId == employee.Id).ToList();
                var utilization = GetEmployeeUtilization(employee.Id, employeeTasks);
                metrics.EmployeeMetrics.Add(new EmployeeMetric
                {
                    EmployeeId = employee.Id,
                    EmployeeName = employee.FullName,
                    AssignedTasks = employeeTasks.Count,
                    CompletedTasks = employeeTasks.Count(t => t.Status == TaskStatus.Done),
                    Utilization = utilization,
                    Productivity = employeeTasks.Any() ? (decimal)employeeTasks.Count(t => t.Status == TaskStatus.Done) / employeeTasks.Count * 100 : 0
                });
            }

            metrics.AverageTeamUtilization = metrics.EmployeeMetrics.Any() ? metrics.EmployeeMetrics.Average(e => e.Utilization) : 0;

            // Sprint velocity
            var sprints = await _context.Sprints.Include(s => s.Tasks).ToListAsync();
            var velocities = sprints.Select(s => s.Tasks.Where(t => t.Status == TaskStatus.Done).Sum(t => t.EstimatedHours)).ToList();
            metrics.AverageSprintVelocity = velocities.Any() ? velocities.Average() : 0;

            // Recent sprints
            metrics.RecentSprints = sprints.OrderByDescending(s => s.EndDate).Take(5).Select(s => new SprintMetric
            {
                SprintId = s.Id,
                SprintName = s.Name,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                TotalTasks = s.Tasks.Count,
                CompletedTasks = s.Tasks.Count(t => t.Status == TaskStatus.Done),
                Velocity = s.Tasks.Where(t => t.Status == TaskStatus.Done).Sum(t => t.EstimatedHours),
                Completion = s.Tasks.Any() ? (decimal)s.Tasks.Count(t => t.Status == TaskStatus.Done) / s.Tasks.Count * 100 : 0,
                Status = s.IsActive ? "Active" : (DateTime.Now > s.EndDate ? "Completed" : "Planning")
            }).ToList();

            // Advanced Analytics Metrics
            metrics.TaskStatusDistribution = allTasks
                .GroupBy(t => t.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            metrics.EmployeeWorkload = allUsers
                .ToDictionary(
                    u => !string.IsNullOrEmpty(u.FullName) ? u.FullName : (u.Email ?? "Unknown User"),
                    u => allTasks.Where(t => t.AssigneeId == u.Id && t.Status != TaskStatus.Done).Sum(t => t.EstimatedHours))
                .Where(kv => kv.Value > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            metrics.TopBlockers = allTasks
                .Where(t => t.DueDate.HasValue && t.DueDate < DateTime.Now && t.Status != TaskStatus.Done)
                .OrderByDescending(t => t.EstimatedHours)
                .Take(5)
                .Select(t => t.Title)
                .ToList();

            return metrics;
        }

        private async Task<ProjectLeadDashboard> GetProjectLeadMetrics(string userId, List<int> projectIds)
        {
            var metrics = new ProjectLeadDashboard();

            var projects = await _context.Projects
                .Where(p => projectIds.Contains(p.Id))
                .Include(p => p.Sprints)
                .ToListAsync();

            if (projects.Count > 0)
            {
                var firstProject = projects.First();
                metrics.ProjectId = firstProject.Id;
                metrics.ProjectName = firstProject.Name;

                var sprints = await _context.Sprints
                    .Where(s => s.ProjectId == firstProject.Id)
                    .Include(s => s.Tasks)
                    .ThenInclude(t => t.Assignee)
                    .ToListAsync();

                metrics.TotalSprints = sprints.Count;
                metrics.ActiveSprints = sprints.Count(s => s.IsActive);
                metrics.CompletedSprints = sprints.Count(s => !s.IsActive && sprints.All(sp => !sp.IsActive));

                var allProjectTasks = await _context.Tasks
                    .Where(t => t.ProjectId == firstProject.Id)
                    .Include(t => t.Assignee)
                    .ToListAsync();

                metrics.TotalTasks = allProjectTasks.Count;
                metrics.CompletedTasks = allProjectTasks.Count(t => t.Status == TaskStatus.Done);
                metrics.InProgressTasks = allProjectTasks.Count(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested);
                metrics.BlockedTasks = metrics.InProgressTasks > 0 && allProjectTasks.Count(t => (t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested) && t.DueDate.HasValue && t.DueDate < DateTime.Now) > 0 
                    ? allProjectTasks.Count(t => (t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested) && t.DueDate.HasValue && t.DueDate < DateTime.Now) 
                    : 0;
                metrics.ProjectCompletion = allProjectTasks.Any() ? (decimal)metrics.CompletedTasks / metrics.TotalTasks * 100 : 0;
                metrics.TeamSize = allProjectTasks.Select(t => t.AssigneeId).Distinct().Count();

                // Team member metrics
                var teamMembers = allProjectTasks.Select(t => t.AssigneeId).Distinct().ToList();
                foreach (var memberId in teamMembers)
                {
                    var memberTasks = allProjectTasks.Where(t => t.AssigneeId == memberId).ToList();
                    var member = await _context.Users.FindAsync(memberId);
                    metrics.TeamMetrics.Add(new TeamMemberMetric
                    {
                        MemberId = memberId,
                        MemberName = member?.FullName ?? "Unknown",
                        AssignedTasks = memberTasks.Count,
                        CompletedTasks = memberTasks.Count(t => t.Status == TaskStatus.Done),
                        Utilization = GetEmployeeUtilization(memberId, memberTasks)
                    });
                }

                // Sprint metrics
                foreach (var sprint in sprints)
                {
                    var sprintVelocity = sprint.Tasks.Where(t => t.Status == TaskStatus.Done).Sum(t => t.EstimatedHours);
                    metrics.SprintMetrics.Add(new SprintMetric
                    {
                        SprintId = sprint.Id,
                        SprintName = sprint.Name,
                        StartDate = sprint.StartDate,
                        EndDate = sprint.EndDate,
                        TotalTasks = sprint.Tasks.Count,
                        CompletedTasks = sprint.Tasks.Count(t => t.Status == TaskStatus.Done),
                        Velocity = sprintVelocity,
                        Completion = sprint.Tasks.Any() ? (decimal)sprint.Tasks.Count(t => t.Status == TaskStatus.Done) / sprint.Tasks.Count * 100 : 0,
                        Status = sprint.IsActive ? "Active" : "Completed"
                    });
                }
                // Advanced Analytics Metrics
                metrics.TaskStatusDistribution = allProjectTasks
                    .GroupBy(t => t.Status.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());

                metrics.EmployeeWorkload = teamMembers
                    .ToDictionary(
                        id => allProjectTasks.FirstOrDefault(t => t.AssigneeId == id)?.Assignee?.FullName ?? "Unknown",
                        id => allProjectTasks.Where(t => t.AssigneeId == id && t.Status != TaskStatus.Done).Sum(t => t.EstimatedHours))
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            return metrics;
        }

        private async Task<CoordinatorDashboard> GetCoordinatorMetrics()
        {
            var metrics = new CoordinatorDashboard();

            var allTasks = await _context.Tasks
                .Include(t => t.Sprint)
                .Include(t => t.Project)
                .Include(t => t.Assignee)
                .ToListAsync();

            metrics.TotalTasks = allTasks.Count;
            metrics.CompletedTasks = allTasks.Count(t => t.Status == TaskStatus.Done);
            metrics.InProgressTasks = allTasks.Count(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested);
            metrics.ToDoTasks = allTasks.Count(t => t.Status == TaskStatus.ToDo || t.Status == TaskStatus.Approved || t.Status == TaskStatus.New);
            metrics.UnassignedTasks = allTasks.Count(t => t.AssigneeId == null);
            metrics.OverdueTasks = allTasks.Count(t => t.DueDate.HasValue && t.DueDate < DateTime.Now && t.Status != TaskStatus.Done);
            metrics.TaskCompletionRate = allTasks.Any() ? (decimal)metrics.CompletedTasks / allTasks.Count * 100 : 0;

            // Current sprints
            var activeSprints = await _context.Sprints
                .Where(s => s.IsActive)
                .Include(s => s.Tasks)
                .ToListAsync();

            metrics.CurrentSprints = activeSprints.Count;

            // Sprint progress
            foreach (var sprint in activeSprints)
            {
                var sprintTasks = sprint.Tasks.ToList();
                var daysRemaining = (sprint.EndDate.Date - DateTime.Now.Date).Days;
                metrics.SprintProgress.Add(new SprintProgressMetric
                {
                    SprintId = sprint.Id,
                    SprintName = sprint.Name,
                    TotalTasks = sprintTasks.Count,
                    CompletedTasks = sprintTasks.Count(t => t.Status == TaskStatus.Done),
                    Progress = sprintTasks.Any() ? (decimal)sprintTasks.Count(t => t.Status == TaskStatus.Done) / sprintTasks.Count * 100 : 0,
                    EndDate = sprint.EndDate,
                    DaysRemaining = Math.Max(0, daysRemaining)
                });
            }

            // Upcoming deadlines
            var upcomingTasks = allTasks
                .Where(t => t.DueDate.HasValue && t.DueDate > DateTime.Now && t.DueDate < DateTime.Now.AddDays(7) && t.Status != TaskStatus.Done)
                .OrderBy(t => t.DueDate)
                .Take(10)
                .Select(t => new TaskMetric
                {
                    TaskId = t.Id,
                    TaskTitle = t.Title,
                    DueDate = t.DueDate ?? DateTime.Now,
                    AssigneeName = t.Assignee?.FullName ?? "Unassigned",
                    Status = t.Status.ToString()
                })
                .ToList();

            metrics.UpcomingDeadlines = upcomingTasks;

            return metrics;
        }

        private async Task<EmployeeDashboard> GetEmployeeMetrics(string userId, string employeeName)
        {
            var metrics = new EmployeeDashboard
            {
                EmployeeName = employeeName
            };

            var myTasks = await _context.Tasks
                .Where(t => t.AssigneeId == userId)
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .ToListAsync();

            metrics.AssignedTasks = myTasks.Count;
            metrics.CompletedTasks = myTasks.Count(t => t.Status == TaskStatus.Done);
            metrics.InProgressTasks = myTasks.Count(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested);
            metrics.ToDoTasks = myTasks.Count(t => t.Status == TaskStatus.ToDo || t.Status == TaskStatus.Approved || t.Status == TaskStatus.New);
            metrics.TaskCompletion = myTasks.Any() ? (decimal)metrics.CompletedTasks / myTasks.Count * 100 : 0;
            metrics.CurrentWeekload = myTasks.Where(t => t.Status != TaskStatus.Done).Sum(t => t.EstimatedHours);
            metrics.MyProjects = myTasks.Select(t => t.Project?.Name).Distinct().Where(n => n != null).Cast<string>().ToList();
            metrics.CurrentSprints = myTasks.Select(t => t.SprintId).Distinct().Count(s => s.HasValue);

            // My tasks list
            metrics.MyTasks = myTasks
                .OrderBy(t => t.DueDate)
                .Select(t => new PersonalTaskMetric
                {
                    TaskId = t.Id,
                    TaskTitle = t.Title,
                    ProjectName = t.Project?.Name ?? "No Project",
                    SprintName = t.Sprint?.Name ?? "No Sprint",
                    Status = t.Status.ToString(),
                    DueDate = t.DueDate,
                    EstimatedHours = t.EstimatedHours
                })
                .ToList();

            return metrics;
        }

        private decimal GetEmployeeUtilization(string employeeId, List<Models.TaskItem> tasks)
        {
            if (!tasks.Any()) return 0;

            var inProgressHours = tasks.Where(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Committed || t.Status == TaskStatus.Tested).Sum(t => t.EstimatedHours);
            var totalHours = tasks.Sum(t => t.EstimatedHours);
 
            return totalHours > 0 ? (inProgressHours / totalHours) * 100 : 0;
        }

        private string DetermineProjectStatus(List<Models.TaskItem> tasks)
        {
            if (!tasks.Any()) return "Not Started";

            var overdueTasks = tasks.Count(t => t.DueDate.HasValue && t.DueDate < DateTime.Now && t.Status != TaskStatus.Done);
            if (overdueTasks > 0) return "At Risk";

            var completion = (decimal)tasks.Count(t => t.Status == TaskStatus.Done) / tasks.Count * 100;
            if (completion > 75) return "On Track";

            return "On Track";
        }
    }
}

