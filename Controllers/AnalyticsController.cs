using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.ViewModels.Analytics;

using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager,Project Lead,Project Coordinator")]
    public class AnalyticsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? assigneeId, int? projectId)
        {
            var vm = new DashboardViewModel
            {
                SelectedAssigneeId = assigneeId,
                SelectedProjectId = projectId
            };

            var tasksQuery = _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.Sprint)
                .AsQueryable();

            if (!string.IsNullOrEmpty(assigneeId))
            {
                tasksQuery = tasksQuery.Where(t => t.AssigneeId == assigneeId);
            }

            if (projectId.HasValue)
            {
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
            }

            var tasks = await tasksQuery.ToListAsync();

            var assigneesList = await _context.Users.ToListAsync();
            var projectsList = await _context.Projects.ToListAsync();

            vm.Assignees = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(assigneesList, "Id", "Email", assigneeId);
            vm.Projects = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(projectsList, "Id", "Name", projectId);

            // Calculate Engagements
            foreach (var task in tasks.Where(t => t.AssigneeId != null && t.StartDate.HasValue && t.EstimatedHours > 0))
            {
                // Simple daily distribution: assume 8 hours per day
                int daysRequired = (int)Math.Ceiling(task.EstimatedHours / 8.0m);
                decimal hoursLeft = task.EstimatedHours;
                
                for (int i = 0; i < daysRequired; i++)
                {
                    decimal hoursToday = Math.Min(8.0m, hoursLeft);
                    
                    vm.Engagements.Add(new DailyEngagement
                    {
                        AssigneeName = task.Assignee!.Email,
                        TaskTitle = task.Title,
                        Date = task.StartDate!.Value.Date.AddDays(i),
                        Hours = hoursToday,
                        IsToDo = task.Status == Models.Enums.TaskStatus.ToDo
                    });

                    hoursLeft -= hoursToday;
                    if (hoursLeft <= 0) break;
                }
            }

            // Calculate Burndown per Sprint
            var sprints = await _context.Sprints.Include(s => s.Tasks).ToListAsync();
            foreach (var sprint in sprints)
            {
                var totalEstimated = sprint.Tasks.Sum(t => t.EstimatedHours);
                var sprintTasks = sprint.Tasks.ToList();

                // Simplify burndown to total hours vs elapsed days
                if (sprint.StartDate < sprint.EndDate && totalEstimated > 0)
                {
                    var totalDays = (sprint.EndDate.Date - sprint.StartDate.Date).Days;
                    for (int i = 0; i <= totalDays; i++)
                    {
                        var date = sprint.StartDate.Date.AddDays(i);
                        // Hours remaining on this date
                        // For simplicity MVP, assume tasks marked Done before/on this date decrease the remaining hours
                        // Proper burndown would use TaskHistory to see when status changed to Done
                        var completedTasksBeforeDate = sprintTasks.Where(t => t.Status == Models.Enums.TaskStatus.Done && (t.DueDate <= date || t.CreatedAt <= date)).Sum(t => t.EstimatedHours);
                        // Approximation without full historical playback:
                        
                        // Fallback: simple line drawing for MVP
                        decimal remaining = totalEstimated - (totalEstimated / totalDays) * i;
                        if(remaining < 0) remaining = 0;

                        vm.Burndowns.Add(new SprintBurndown
                        {
                            SprintName = sprint.Name,
                            Date = date,
                            RemainingHours = remaining
                        });
                    }
                }
                
                // Calculate Velocity
                var completedHours = sprint.Tasks.Where(t => t.Status == TaskStatus.Done).Sum(t => t.EstimatedHours);
                vm.Velocities.Add(new SprintVelocity
                {
                    SprintName = sprint.Name,
                    CompletedHours = completedHours
                });
            }

            return View(vm);
        }
    }
}
