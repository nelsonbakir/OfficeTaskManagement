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

            return View(vm);
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
            metrics.AtRiskTasks = allTasks.Count(t => t.Status == TaskStatus.InProgress && t.DueDate.HasValue && t.DueDate < DateTime.Now.AddDays(3));

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
                metrics.InProgressTasks = allProjectTasks.Count(t => t.Status == TaskStatus.InProgress);
                metrics.BlockedTasks = allProjectTasks.Count(t => t.Status == TaskStatus.InProgress && t.DueDate.HasValue && t.DueDate < DateTime.Now);
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
            metrics.InProgressTasks = allTasks.Count(t => t.Status == TaskStatus.InProgress);
            metrics.ToDoTasks = allTasks.Count(t => t.Status == TaskStatus.ToDo);
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
            metrics.InProgressTasks = myTasks.Count(t => t.Status == TaskStatus.InProgress);
            metrics.ToDoTasks = myTasks.Count(t => t.Status == TaskStatus.ToDo);
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

            var inProgressHours = tasks.Where(t => t.Status == TaskStatus.InProgress).Sum(t => t.EstimatedHours);
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

