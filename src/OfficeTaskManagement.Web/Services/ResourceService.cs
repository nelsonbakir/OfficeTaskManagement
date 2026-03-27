using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    public class ResourceService : IResourceService
    {
        private readonly ApplicationDbContext _context;

        public ResourceService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <inheritdoc/>
        public async Task<ResourceProfile> GetOrCreateProfileAsync(string userId)
        {
            var profile = await _context.ResourceProfiles
                .Include(rp => rp.Skills)
                .FirstOrDefaultAsync(rp => rp.UserId == userId);

            if (profile == null)
            {
                profile = new ResourceProfile
                {
                    UserId = userId,
                    DailyCapacityHours = 8,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ResourceProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }

            return profile;
        }

        /// <inheritdoc/>
        public async Task<decimal> GetUserAvailableHoursAsync(string userId, DateTime startDate, DateTime endDate)
        {
            var profile = await GetOrCreateProfileAsync(userId);

            // Count working days (Fri-Sat weekend in Bangladesh) in range, excluding holidays
            var workingDays = await CountWorkingDaysAsync(startDate, endDate);
            var grossHours = workingDays * profile.DailyCapacityHours;

            // Subtract specific availability blocks (leave, etc.)
            var blocks = await _context.ResourceAvailabilityBlocks
                .Where(b => b.UserId == userId && b.StartDate.Date <= endDate.Date && b.EndDate.Date >= startDate.Date)
                .ToListAsync();

            decimal blockedHours = 0;
            foreach (var block in blocks)
            {
                // Clamp block to range
                var blockStart = block.StartDate > startDate ? block.StartDate : startDate;
                var blockEnd = block.EndDate < endDate ? block.EndDate : endDate;
                if (blockEnd >= blockStart)
                {
                    blockedHours += (await CountWorkingDaysAsync(blockStart, blockEnd)) * profile.DailyCapacityHours;
                }
            }

            return Math.Max(0, grossHours - blockedHours);
        }

        /// <inheritdoc/>
        public async Task<int> GetUserTotalAllocationPercentAsync(string userId, DateTime? date = null)
        {
            var checkDate = (date ?? DateTime.UtcNow).Date;
            var allocations = await _context.ProjectResourceAllocations
                .Where(a => a.UserId == userId
                         && a.StartDate.Date <= checkDate
                         && (a.EndDate == null || a.EndDate.Value.Date >= checkDate))
                .ToListAsync();

            return allocations.Sum(a => a.AllocationPercentage);
        }

        /// <inheritdoc/>
        public async Task<bool> IsUserOverAllocatedAsync(string userId, DateTime startDate, DateTime endDate)
        {
            var allocations = await _context.ProjectResourceAllocations
                .Where(a => a.UserId == userId
                         && a.StartDate.Date <= endDate.Date
                         && (a.EndDate == null || a.EndDate.Value.Date >= startDate.Date))
                .ToListAsync();

            // Fix #11: Load public holidays for the period to exclude them
            var holidays = await _context.PublicHolidays
                .Where(h => h.Date.Date >= startDate.Date && h.Date.Date <= endDate.Date)
                .Select(h => h.Date.Date)
                .ToListAsync();

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                // Fix #1: Bangladesh weekends are Friday and Saturday
                if (day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday) continue;
                // Fix #11: skip public holidays
                if (holidays.Contains(day)) continue;

                var dayTotal = allocations
                    .Where(a => a.StartDate.Date <= day && (a.EndDate == null || a.EndDate.Value.Date >= day))
                    .Sum(a => a.AllocationPercentage);

                if (dayTotal > 100) return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<AllocationSummaryItem>> GetUserAllocationSummaryAsync(string userId)
        {
            return await _context.ProjectResourceAllocations
                .Where(a => a.UserId == userId)
                .Include(a => a.Project)
                .Select(a => new AllocationSummaryItem
                {
                    AllocationId = a.Id,
                    ProjectName = a.Project != null ? a.Project.Name : "(Unknown)",
                    AllocationPercentage = a.AllocationPercentage,
                    StartDate = a.StartDate,
                    EndDate = a.EndDate,
                    ProjectRole = a.ProjectRole
                })
                .ToListAsync();
        }

        /// <inheritdoc/>
        public async Task<decimal> GetUserUtilizationPercentAsync(string userId, int year, int month)
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var availableHours = await GetUserAvailableHoursAsync(userId, startDate, endDate);
            if (availableHours <= 0) return 0;

            var profile = await GetOrCreateProfileAsync(userId);

            // 1. Get Project Allocations
            var allocations = await _context.ProjectResourceAllocations
                .Where(a => a.UserId == userId && a.StartDate.Date <= endDate.Date && (a.EndDate == null || a.EndDate.Value.Date >= startDate.Date))
                .ToListAsync();

            var projectAllocatedHours = new Dictionary<int, decimal>();
            foreach (var alloc in allocations)
            {
                var allocStart = alloc.StartDate > startDate ? alloc.StartDate : startDate;
                var allocEnd = (alloc.EndDate.HasValue && alloc.EndDate.Value < endDate) ? alloc.EndDate.Value : endDate;
                
                if (allocEnd >= allocStart)
                {
                    var days = await CountWorkingDaysAsync(allocStart, allocEnd);
                    var hours = days * profile.DailyCapacityHours * (alloc.AllocationPercentage / 100m);
                    if (!projectAllocatedHours.ContainsKey(alloc.ProjectId)) projectAllocatedHours[alloc.ProjectId] = 0;
                    projectAllocatedHours[alloc.ProjectId] += hours;
                }
            }

            // 2. Get Task-based Estimates (for tasks active this month)
            // Fix #2: Added .Where(t.ProjectId != null) to prevent NullReferenceException
            var tasks = await _context.Tasks
                .Where(t => t.AssigneeId == userId
                         && t.ProjectId != null
                         && t.Status != Models.Enums.TaskStatus.Done
                         && t.Status != Models.Enums.TaskStatus.Approved)
                .ToListAsync();

            var projectTaskHours = new Dictionary<int, decimal>();
            foreach (var task in tasks)
            {
                if (!projectTaskHours.ContainsKey(task.ProjectId.Value)) projectTaskHours[task.ProjectId.Value] = 0;
                projectTaskHours[task.ProjectId.Value] += (decimal)task.EstimatedHours;
            }

            // 3. Combine: Use Max(Allocation, Tasks) per project to avoid double counting 
            // but reflect over-assignment of tasks.
            decimal totalAllocatedHours = 0;
            var allProjectIds = projectAllocatedHours.Keys.Union(projectTaskHours.Keys);
            
            foreach (var pid in allProjectIds)
            {
                decimal allocH = projectAllocatedHours.TryGetValue(pid, out var h1) ? h1 : 0;
                decimal taskH = projectTaskHours.TryGetValue(pid, out var h2) ? h2 : 0;
                totalAllocatedHours += Math.Max(allocH, taskH);
            }

            return Math.Round((totalAllocatedHours / availableHours) * 100, 1);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<UserUtilizationDto>> GetTeamUtilizationAsync(int year, int month)
        {
            // Fix #17: Only include users with IsResource=true (excludes external stakeholders)
            var users = await _context.Users
                .Include(u => u.ResourceProfile)
                .Where(u => u.ResourceProfile != null && u.ResourceProfile.IsResource)
                .ToListAsync();

            var result = new List<UserUtilizationDto>();
            foreach (var user in users)
            {
                var startDate = new DateTime(year, month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var available = await GetUserAvailableHoursAsync(user.Id, startDate, endDate);
                var utilPercent = await GetUserUtilizationPercentAsync(user.Id, year, month);
                var allocated = available * (utilPercent / 100m);

                result.Add(new UserUtilizationDto
                {
                    UserId = user.Id,
                    FullName = user.FullName ?? user.Email ?? user.Id,
                    Department = user.ResourceProfile?.Department,
                    AvailableHours = available,
                    AllocatedHours = Math.Round(allocated, 1),
                    UtilizationPercent = utilPercent
                });
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<ProjectCostReport>> GetProjectCostReportAsync()
        {
            var allocations = await _context.ProjectResourceAllocations
                .Include(a => a.Project)
                .Include(a => a.ResourceProfile)
                .ToListAsync();

            // For each allocation, calculate hours committed × hourly rate
            var projectGroups = allocations.GroupBy(a => a.ProjectId);
            var result = new List<ProjectCostReport>();

            foreach (var group in projectGroups)
            {
                decimal totalCost = 0;
                decimal totalHours = 0;
                var distinctResources = new HashSet<string>();

                foreach (var alloc in group)
                {
                    var endDate = alloc.EndDate ?? DateTime.UtcNow;
                    if (endDate < alloc.StartDate) continue;

                    var days = await CountWorkingDaysAsync(alloc.StartDate, endDate);
                    var dailyHours = alloc.ResourceProfile?.DailyCapacityHours ?? 8m;
                    var hours = days * dailyHours * (alloc.AllocationPercentage / 100m);
                    var rate = alloc.ResourceProfile?.HourlyRate ?? 0m;

                    totalHours += hours;
                    totalCost  += hours * rate;
                    distinctResources.Add(alloc.UserId);
                }

                result.Add(new ProjectCostReport
                {
                    ProjectId            = group.Key,
                    ProjectName          = group.First().Project?.Name ?? "(Unknown)",
                    EstimatedLaborCost   = Math.Round(totalCost,  2),
                    TotalAllocatedHours  = Math.Round(totalHours, 1),
                    ResourceCount        = distinctResources.Count
                });
            }

            return result.OrderByDescending(r => r.EstimatedLaborCost);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> GetSprintAssigneeIdsAsync(int sprintId)
        {
            return await _context.Tasks
                .Where(t => t.SprintId == sprintId && t.AssigneeId != null)
                .Select(t => t.AssigneeId!)
                .Distinct()
                .ToListAsync();
        }

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
