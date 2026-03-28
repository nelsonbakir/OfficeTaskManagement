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

            // Subtract specific availability blocks (leave, etc.) that are Approved
            var blocks = await _context.ResourceAvailabilityBlocks
                .Where(b => b.UserId == userId 
                         && b.StartDate.Date <= endDate.Date 
                         && b.EndDate.Date >= startDate.Date
                         && b.ApprovalStatus == OfficeTaskManagement.Models.Enums.LeaveApprovalStatus.Approved)
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
        public async Task<int> GetPeakAllocationPercentInRangeAsync(string userId, DateTime startDate, DateTime endDate)
        {
            var allocations = await _context.ProjectResourceAllocations
                .Where(a => a.UserId == userId
                         && a.StartDate.Date <= endDate.Date
                         && (a.EndDate == null || a.EndDate.Value.Date >= startDate.Date))
                .ToListAsync();

            var holidays = await _context.PublicHolidays
                .Where(h => h.Date.Date >= startDate.Date && h.Date.Date <= endDate.Date)
                .Select(h => h.Date.Date)
                .ToListAsync();

            var peak = 0;
            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday)
                    continue;
                if (holidays.Contains(day))
                    continue;

                var dayTotal = allocations
                    .Where(a => a.StartDate.Date <= day && (a.EndDate == null || a.EndDate.Value.Date >= day))
                    .Sum(a => a.AllocationPercentage);

                if (dayTotal > peak)
                    peak = (int)Math.Min(dayTotal, 500);
            }

            return peak;
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
                    ProjectId = a.ProjectId,
                    ProjectName = a.Project != null ? a.Project.Name : "(Unknown)",
                    AllocationPercentage = a.AllocationPercentage,
                    StartDate = a.StartDate,
                    EndDate = a.EndDate,
                    ProjectRole = a.ProjectRole
                })
                .ToListAsync();
        }

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

            // 2. Get Task-based Estimates with Hierarchical Resolution
            var allTasks = await _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Epic)
                .Include(t => t.Feature).ThenInclude(f => f.Epic)
                .Include(t => t.UserStory).ThenInclude(us => us.Feature).ThenInclude(f => f.Epic)
                .Where(t => t.AssigneeId == userId
                         && t.Status != Models.Enums.TaskStatus.Done
                         && t.Status != Models.Enums.TaskStatus.Approved)
                .ToListAsync();

            var projectTaskHours = new Dictionary<int, decimal>();
            foreach (var t in allTasks)
            {
                int? resolvedId = t.ProjectId 
                               ?? t.Sprint?.ProjectId 
                               ?? t.Epic?.ProjectId 
                               ?? t.Feature?.Epic?.ProjectId 
                               ?? t.UserStory?.Feature?.Epic?.ProjectId;

                if (resolvedId.HasValue)
                {
                    if (!projectTaskHours.ContainsKey(resolvedId.Value)) projectTaskHours[resolvedId.Value] = 0;
                    projectTaskHours[resolvedId.Value] += (decimal)t.EstimatedHours;
                }
            }

            // 3. Combine: Use Max(Allocation, Tasks) per project to avoid double counting 
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
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // 1. Fetch resources
            var users = await _context.Users
                .Include(u => u.ResourceProfile)
                .Where(u => u.ResourceProfile != null && u.ResourceProfile.IsResource)
                .ToListAsync();

            var result = new List<UserUtilizationDto>();

            // 2. Process each user (optimized)
            foreach (var user in users)
            {
                var available = await GetUserAvailableHoursAsync(user.Id, startDate, endDate);
                if (available <= 0) continue;

                var profile = user.ResourceProfile!;

                // A. Strategic Allocations
                var allocations = await _context.ProjectResourceAllocations
                    .Where(a => a.UserId == user.Id && a.StartDate.Date <= endDate.Date && (a.EndDate == null || a.EndDate.Value.Date >= startDate.Date))
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

                // B. Operational Tasks (Hierarchical)
                var allTasks = await _context.Tasks
                    .Include(t => t.Sprint)
                    .Include(t => t.Epic)
                    .Include(t => t.Feature).ThenInclude(f => f.Epic)
                    .Include(t => t.UserStory).ThenInclude(us => us.Feature).ThenInclude(f => f.Epic)
                    .Where(t => t.AssigneeId == user.Id && t.Status != Models.Enums.TaskStatus.Done && t.Status != Models.Enums.TaskStatus.Approved)
                    .ToListAsync();

                var projectTaskHours = new Dictionary<int, decimal>();
                foreach (var t in allTasks)
                {
                    int? resId = t.ProjectId ?? t.Sprint?.ProjectId ?? t.Epic?.ProjectId ?? t.Feature?.Epic?.ProjectId ?? t.UserStory?.Feature?.Epic?.ProjectId;
                    if (resId.HasValue)
                    {
                        if (!projectTaskHours.ContainsKey(resId.Value)) projectTaskHours[resId.Value] = 0;
                        projectTaskHours[resId.Value] += (decimal)t.EstimatedHours;
                    }
                }

                // C. Combine for PMP "Total Load"
                decimal totalSafetyHours = 0;
                var allPids = projectAllocatedHours.Keys.Union(projectTaskHours.Keys);
                foreach (var pid in allPids)
                {
                    decimal h1 = projectAllocatedHours.TryGetValue(pid, out var v1) ? v1 : 0;
                    decimal h2 = projectTaskHours.TryGetValue(pid, out var v2) ? v2 : 0;
                    totalSafetyHours += Math.Max(h1, h2);
                }

                result.Add(new UserUtilizationDto
                {
                    UserId = user.Id,
                    FullName = user.FullName ?? user.Email ?? user.Id,
                    Department = profile.Department,
                    AvailableHours = available,
                    AllocatedHours = Math.Round(projectAllocatedHours.Values.Sum(), 1),
                    TaskDemandHours = Math.Round(projectTaskHours.Values.Sum(), 1),
                    UtilizationPercent = Math.Round((totalSafetyHours / available) * 100, 1)
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

            var allTasks = await _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Sprint)
                .Include(t => t.Epic)
                .Include(t => t.Feature).ThenInclude(f => f.Epic)
                .Include(t => t.UserStory).ThenInclude(us => us.Feature).ThenInclude(f => f.Epic)
                .Include(t => t.Assignee).ThenInclude(u => u.ResourceProfile)
                .ToListAsync();

            // Resolve actual ProjectId for each task
            var resolvedTasks = allTasks.Select(t => {
                int? resolvedId = t.ProjectId 
                               ?? t.Sprint?.ProjectId 
                               ?? t.Epic?.ProjectId 
                               ?? t.Feature?.Epic?.ProjectId 
                               ?? t.UserStory?.Feature?.Epic?.ProjectId;
                
                return new { Task = t, ResolvedProjectId = resolvedId };
            }).Where(x => x.ResolvedProjectId.HasValue).ToList();

            // Get all project IDs that have either allocations OR resolved tasks
            var projectIds = allocations.Select(a => a.ProjectId)
                .Union(resolvedTasks.Select(t => t.ResolvedProjectId!.Value))
                .Distinct()
                .ToList();

            var result = new List<ProjectCostReport>();

            foreach (var projectId in projectIds)
            {
                var projectAllocations = allocations.Where(a => a.ProjectId == projectId).ToList();
                var projectTasks = resolvedTasks.Where(t => t.ResolvedProjectId == projectId).Select(t => t.Task).ToList();
                
                string projectName = projectAllocations.FirstOrDefault()?.Project?.Name 
                                  ?? projectTasks.FirstOrDefault()?.Project?.Name 
                                  ?? "(Unknown)";

                decimal plannedValuePV = 0;
                decimal totalAllocatedHours = 0;
                var distinctResources = new HashSet<string>();

                // 1. Calculate PV from high-level Allocations
                foreach (var alloc in projectAllocations)
                {
                    var endDate = alloc.EndDate ?? DateTime.UtcNow;
                    if (endDate < alloc.StartDate) continue;

                    var days = await CountWorkingDaysAsync(alloc.StartDate, endDate);
                    var dailyHours = alloc.ResourceProfile?.DailyCapacityHours ?? 8m;
                    var hours = days * dailyHours * (alloc.AllocationPercentage / 100m);
                    var rate = alloc.ResourceProfile?.HourlyRate ?? 0m;

                    totalAllocatedHours += hours;
                    plannedValuePV += hours * rate;
                    distinctResources.Add(alloc.UserId);
                }

                // 2. Calculate Bottom-Up Estimate (EAC) from individual Tasks
                decimal bottomUpEstimateEAC = 0;
                decimal totalTaskHours = 0;

                foreach (var task in projectTasks)
                {
                    var rate = task.Assignee?.ResourceProfile?.HourlyRate ?? 0m;
                    var taskH = (decimal)task.EstimatedHours;
                    totalTaskHours += taskH;
                    bottomUpEstimateEAC += taskH * rate;
                    if (task.AssigneeId != null) distinctResources.Add(task.AssigneeId);
                }

                result.Add(new ProjectCostReport
                {
                    ProjectId            = projectId,
                    ProjectName          = projectName,
                    PlannedValuePV       = Math.Round(plannedValuePV, 2),
                    BottomUpEstimateEAC  = Math.Round(bottomUpEstimateEAC, 2),
                    TotalAllocatedHours  = Math.Round(totalAllocatedHours, 1),
                    TotalTaskHours       = Math.Round(totalTaskHours, 1),
                    ResourceCount        = distinctResources.Count
                });
            }

            return result.OrderByDescending(r => Math.Max(r.PlannedValuePV, r.BottomUpEstimateEAC));
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
