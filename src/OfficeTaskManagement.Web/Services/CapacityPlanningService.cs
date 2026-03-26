using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    public class CapacityPlanningService : ICapacityPlanningService
    {
        private readonly ApplicationDbContext _context;
        private readonly IResourceService _resourceService;

        public CapacityPlanningService(ApplicationDbContext context, IResourceService resourceService)
        {
            _context = context;
            _resourceService = resourceService;
        }

        /// <inheritdoc/>
        public async Task<decimal> GetSprintDemandHoursAsync(int sprintId)
        {
            return await _context.Tasks
                .Where(t => t.SprintId == sprintId)
                .SumAsync(t => (decimal?)t.EstimatedHours) ?? 0;
        }

        /// <inheritdoc/>
        public async Task<decimal> GetSprintCapacityHoursAsync(int sprintId)
        {
            var sprint = await _context.Sprints
                .FirstOrDefaultAsync(s => s.Id == sprintId);
            if (sprint == null) return 0;

            if (sprint.PlannedCapacityHours.HasValue && sprint.PlannedCapacityHours.Value > 0)
                return (decimal)sprint.PlannedCapacityHours.Value;

            var assigneeIds = await _context.Tasks
                .Where(t => t.SprintId == sprintId && t.AssigneeId != null)
                .Select(t => t.AssigneeId!)
                .Distinct()
                .ToListAsync();

            decimal total = 0;
            foreach (var userId in assigneeIds)
                total += await _resourceService.GetUserAvailableHoursAsync(userId, sprint.StartDate, sprint.EndDate);

            return total;
        }

        /// <inheritdoc/>
        public async Task<SprintCapacitySummary> GetSprintCapacityVsDemandAsync(int sprintId)
        {
            var sprint = await _context.Sprints
                .FirstOrDefaultAsync(s => s.Id == sprintId);
            if (sprint == null) return new SprintCapacitySummary { SprintId = sprintId };

            var demand = await GetSprintDemandHoursAsync(sprintId);
            var capacity = await GetSprintCapacityHoursAsync(sprintId);

            return new SprintCapacitySummary
            {
                SprintId = sprintId,
                SprintName = sprint.Name,
                StartDate = sprint.StartDate,
                EndDate = sprint.EndDate,
                PlannedCapacityHours = capacity,
                DemandHours = demand
            };
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<SprintCapacitySummary>> GetProjectSprintCapacitiesAsync(int projectId)
        {
            var sprints = await _context.Sprints
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StartDate)
                .ToListAsync();

            var summaries = new List<SprintCapacitySummary>();
            foreach (var sprint in sprints)
            {
                var demand = await GetSprintDemandHoursAsync(sprint.Id);
                var capacity = await GetSprintCapacityHoursAsync(sprint.Id);
                summaries.Add(new SprintCapacitySummary
                {
                    SprintId = sprint.Id,
                    SprintName = sprint.Name,
                    StartDate = sprint.StartDate,
                    EndDate = sprint.EndDate,
                    PlannedCapacityHours = capacity,
                    DemandHours = demand
                });
            }

            return summaries;
        }

        /// <inheritdoc/>
        public async Task<HeatmapData> GetMonthlyHeatmapAsync(int year, int month)
        {
            // Normalize inputs to UTC/Neutral start/end of month
            var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
            
            var daysInMonth = Enumerable.Range(1, DateTime.DaysInMonth(year, month)).ToList();

            // Fix #17: Only include genuine resources (not external stakeholders)
            var users = await _context.Users
                .Include(u => u.ResourceProfile)
                .Include(u => u.AvailabilityBlocks)
                .Include(u => u.ProjectAllocations)
                .Where(u => u.ResourceProfile != null && u.ResourceProfile.IsResource)
                .ToListAsync();

            var allActiveTasks = await _context.Tasks
                .Include(t => t.Sprint)
                .Where(t => t.AssigneeId != null && t.Status != Models.Enums.TaskStatus.Done && t.Status != Models.Enums.TaskStatus.Approved)
                .ToListAsync();

            var holidays = await _context.PublicHolidays
                .Where(h => h.Date.Year == year && h.Date.Month == month)
                .Select(h => h.Date.Date)
                .ToListAsync();

            var heatmap = new HeatmapData
            {
                Year = year,
                Month = month,
                Days = daysInMonth
            };

            foreach (var user in users)
            {
                var profile = user.ResourceProfile;
                var dailyCap = profile?.DailyCapacityHours ?? 8;
                
                var userTasks = allActiveTasks.Where(t => t.AssigneeId == user.Id).ToList();

                var row = new HeatmapRow
                {
                    UserId = user.Id,
                    FullName = user.FullName ?? user.Email ?? user.Id
                };

                foreach (var day in daysInMonth)
                {
                    // Create date object for comparison (normalized)
                    var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

                    // Bangladesh Weekends: Friday and Saturday OR Public Holiday
                    if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || holidays.Contains(date.Date))
                    {
                        row.DailyAvailability[day] = 0;
                        row.DailyAllocation[day] = 0;
                        continue;
                    }

                    // Check availability blocks (leave, etc.)
                    bool isBlocked = user.AvailabilityBlocks.Any(b => 
                        b.StartDate.Date <= date.Date && b.EndDate.Date >= date.Date);
                    
                    row.DailyAvailability[day] = isBlocked ? 0 : dailyCap;

                    // 1. Sum current project allocations for this specific day
                    var allocationPct = user.ProjectAllocations
                        .Where(a => a.StartDate.Date <= date.Date && (a.EndDate == null || a.EndDate.Value.Date >= date.Date))
                        .Sum(a => a.AllocationPercentage);

                    // 2. Add task-based "effective" percentage (Dynamic calculation)
                    foreach (var task in userTasks)
                    {
                        // Check if task overlaps this specific day
                        DateTime? tStart = task.StartDate ?? task.Sprint?.StartDate;
                        DateTime? tEnd = task.DueDate ?? task.Sprint?.EndDate;

                        if (tStart.HasValue && tEnd.HasValue)
                        {
                            if (tStart.Value.Date <= date.Date && tEnd.Value.Date >= date.Date)
                            {
                                // Calculate how much daily load this task represents
                                var totalWorkingDaysInTask = await CountWorkingDaysAsync(tStart.Value, tEnd.Value);
                                if (totalWorkingDaysInTask > 0)
                                {
                                    // Spread the estimated hours over the working days
                                    var dailyTaskHours = (decimal)task.EstimatedHours / totalWorkingDaysInTask;
                                    var taskPct = (int)Math.Round((dailyTaskHours / dailyCap) * 100);
                                    
                                    // Add to this day's total allocation
                                    allocationPct += taskPct;
                                }
                            }
                        }
                    }

                    row.DailyAllocation[day] = allocationPct;
                }
                heatmap.Rows.Add(row);
            }

            return heatmap;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private async Task<int> CountWorkingDaysAsync(DateTime start, DateTime end)
        {
            var holidays = await _context.PublicHolidays
                .Where(h => h.Date.Date >= start.Date && h.Date.Date <= end.Date)
                .Select(h => h.Date.Date)
                .ToListAsync();

            int count = 0;
            for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                // Bangladesh Weekends: Friday and Saturday
                if (day.DayOfWeek != DayOfWeek.Friday && day.DayOfWeek != DayOfWeek.Saturday)
                {
                    if (!holidays.Contains(day))
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
