using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    public class CapacityPlanningService : ICapacityPlanningService
    {
        private readonly ApplicationDbContext _context;
        private readonly IResourceService _resourceService;
        private readonly IMemoryCache _cache;

        public CapacityPlanningService(
            ApplicationDbContext context,
            IResourceService resourceService,
            IMemoryCache cache)
        {
            _context = context;
            _resourceService = resourceService;
            _cache = cache;
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
            var cacheKey = $"heatmap:{year}:{month}";
            if (_cache.TryGetValue(cacheKey, out HeatmapData? cached) && cached != null)
                return cached;

            var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
            var daysInMonthCount = DateTime.DaysInMonth(year, month);
            var daysInMonth = Enumerable.Range(1, daysInMonthCount).ToList();

            // 1. Fetch data efficiently
            var users = await _context.Users
                .Include(u => u.ResourceProfile)
                .Include(u => u.AvailabilityBlocks)
                .Include(u => u.ProjectAllocations)
                .Where(u => u.ResourceProfile != null && u.ResourceProfile.IsResource)
                .ToListAsync();

            var allActiveTasksRaw = await _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Epic)
                .Include(t => t.Feature).ThenInclude(f => f.Epic)
                .Include(t => t.UserStory).ThenInclude(us => us.Feature).ThenInclude(f => f.Epic)
                .Where(t => t.AssigneeId != null && t.Status != Models.Enums.TaskStatus.Done && t.Status != Models.Enums.TaskStatus.Approved)
                .ToListAsync();

            var holidays = await _context.PublicHolidays
                .Where(h => h.Date.Year == year && h.Date.Month == month)
                .Select(h => h.Date.Date)
                .ToListAsync();

            // 2. Resolve Tasks (Project, Dates, and pre-calculate daily load)
            var resolvedTasks = new List<ResolvedTaskLoad>();
            foreach (var t in allActiveTasksRaw)
            {
                int? projectId = t.ProjectId 
                              ?? t.Sprint?.ProjectId 
                              ?? t.Epic?.ProjectId 
                              ?? t.Feature?.Epic?.ProjectId 
                              ?? t.UserStory?.Feature?.Epic?.ProjectId;

                DateTime? start = t.StartDate ?? t.Sprint?.StartDate;
                DateTime? end = t.DueDate ?? t.Sprint?.EndDate;

                if (projectId.HasValue && start.HasValue && end.HasValue)
                {
                    // Calculate working days once per task to avoid loops later
                    int workDaysInTask = await CountWorkingDaysAsync(start.Value, end.Value);
                    if (workDaysInTask > 0)
                    {
                        resolvedTasks.Add(new ResolvedTaskLoad
                        {
                            TaskId = t.Id,
                            AssigneeId = t.AssigneeId!,
                            ProjectId = projectId.Value,
                            StartDate = start.Value.Date,
                            EndDate = end.Value.Date,
                            DailyHours = (decimal)t.EstimatedHours / workDaysInTask
                        });
                    }
                }
            }

            var heatmap = new HeatmapData
            {
                Year = year,
                Month = month,
                Days = daysInMonth
            };

            // 3. Generate Heatmap Rows
            foreach (var user in users)
            {
                var row = new HeatmapRow
                {
                    UserId = user.Id,
                    FullName = user.FullName ?? user.Email ?? user.Id
                };

                var dailyCap = user.ResourceProfile?.DailyCapacityHours ?? 8;
                var userTasks = resolvedTasks.Where(rt => rt.AssigneeId == user.Id).ToList();

                foreach (var day in daysInMonth)
                {
                    var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).Date;

                    // Weekend/Holiday check
                    if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || holidays.Contains(date))
                    {
                        row.DailyAvailability[day] = 0;
                        row.DailyAllocation[day] = 0;
                        continue;
                    }

                    // Leave check
                    bool isBlocked = user.AvailabilityBlocks.Any(b => 
                        b.StartDate.Date <= date && b.EndDate.Date >= date && b.ApprovalStatus == Models.Enums.LeaveApprovalStatus.Approved);
                    
                    row.DailyAvailability[day] = isBlocked ? 0 : dailyCap;

                    if (isBlocked)
                    {
                        row.DailyAllocation[day] = 0;
                        continue;
                    }

                    // Unified PMP Logic: Track Strategic (Alloc), Operational (Task), and Combined (Total)
                    var projectStats = new Dictionary<int, (int AllocPct, int TaskPct)>();

                    // Add allocations (Strategic)
                    foreach (var alloc in user.ProjectAllocations.Where(a => a.StartDate.Date <= date && (a.EndDate == null || a.EndDate.Value.Date >= date)))
                    {
                        projectStats[alloc.ProjectId] = (alloc.AllocationPercentage, 0);
                    }

                    // Add tasks (Operational)
                    foreach (var rt in userTasks.Where(rt => rt.StartDate <= date && rt.EndDate >= date))
                    {
                        var taskPct = (int)Math.Round((rt.DailyHours / dailyCap) * 100);
                        if (projectStats.TryGetValue(rt.ProjectId, out var existing))
                        {
                            projectStats[rt.ProjectId] = (existing.AllocPct, existing.TaskPct + taskPct);
                        }
                        else
                        {
                            projectStats[rt.ProjectId] = (0, taskPct);
                        }
                    }

                    // Populate the three separate metrics for the front-end
                    int strategicTotal = projectStats.Values.Sum(s => s.AllocPct);
                    int operationalTotal = projectStats.Values.Sum(s => s.TaskPct);
                    int combinedTotal = projectStats.Values.Sum(s => Math.Max(s.AllocPct, s.TaskPct));

                    row.DailyAllocation[day] = strategicTotal;
                    row.DailyTaskDemand[day] = operationalTotal;
                    row.DailyTotalLoad[day] = combinedTotal;
                }
                heatmap.Rows.Add(row);
            }

            _cache.Set(cacheKey, heatmap, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(15) });
            return heatmap;
        }

        private class ResolvedTaskLoad
        {
            public int TaskId { get; set; }
            public string AssigneeId { get; set; } = string.Empty;
            public int ProjectId { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public decimal DailyHours { get; set; }
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
