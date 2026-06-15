using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Provides compressed PM knowledge snapshots for injection into AI estimation prompts.
    /// Implements the context compression rules from 03_PROMPT_STRATEGY.md:
    /// - Historical accuracy stats (aggregated, not raw rows)
    /// - Average hourly rate from SalaryHistory (per project team)
    /// 
    /// All data is cached via IMemoryCache to reduce DB queries during repeated estimations.
    /// </summary>
    public class PmKnowledgeService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PmKnowledgeService> _logger;

        public PmKnowledgeService(
            ApplicationDbContext db,
            IMemoryCache cache,
            ILogger<PmKnowledgeService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Returns compressed historical accuracy stats for a project + entity type.
        /// Format: "Backend tasks: avg 8h est → 11h actual (38% overrun)\n..."
        /// Cached for 30 minutes to avoid re-querying on every estimation call.
        /// </summary>
        public async Task<string> GetHistoryStatsAsync(
            int? projectId, string entityType, CancellationToken ct = default)
        {
            if (!projectId.HasValue) return string.Empty;

            var cacheKey = $"history-stats:{projectId}:{entityType}";
            if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
                return cached;

            try
            {
                // Get tasks with both estimated and actual hours (completed tasks = reliable data)
                var doneTasks = await _db.Tasks
                    .Where(t => t.ProjectId == projectId
                        && t.Status == Models.Enums.TaskStatus.Done
                        && t.ActualHours.HasValue
                        && t.EstimatedHours > 0)
                    .Select(t => new { t.Type, t.EstimatedHours, t.ActualHours })
                    .Take(50) // Cap to avoid large queries
                    .ToListAsync(ct);

                if (!doneTasks.Any())
                    return "No historical completion data available for this project.";

                var sb = new StringBuilder("Historical estimation accuracy for this project:\n");
                var avg = doneTasks.Average(t =>
                    (double)((t.ActualHours ?? 0) - t.EstimatedHours) / (double)t.EstimatedHours * 100);
                sb.AppendLine($"- Overall: avg {avg:F0}% overrun ({doneTasks.Count} completed tasks)");

                // Most similar recent item
                var recent = doneTasks.LastOrDefault();
                if (recent != null)
                    sb.AppendLine($"- Recent example: estimated {recent.EstimatedHours:F0}h, actual {recent.ActualHours:F0}h");

                // Team velocity (last 6 sprints)
                var sprints = await _db.Sprints
                    .Where(s => s.ProjectId == projectId)
                    .OrderByDescending(s => s.StartDate)
                    .Take(6)
                    .Select(s => new
                    {
                        s.Name,
                        TaskCount = _db.Tasks.Count(t => t.SprintId == s.Id
                            && t.Status == Models.Enums.TaskStatus.Done)
                    })
                    .ToListAsync(ct);

                if (sprints.Any())
                    sb.AppendLine($"- Team velocity: avg {sprints.Average(s => s.TaskCount):F0} tasks/sprint (last {sprints.Count} sprints)");

                var result = sb.ToString();
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving history stats for project {ProjectId}", projectId);
                return string.Empty;
            }
        }

        /// <summary>
        /// Calculates the average hourly rate (BDT) for team members allocated to a project.
        /// Uses the latest SalaryHistory entry per user: monthlySalary / 22 workdays / 8 hours.
        /// Cached for 15 minutes.
        /// Fallback: 800 BDT/hr if no salary data available.
        /// </summary>
        public async Task<decimal> GetAverageHourlyRateBdtAsync(int projectId)
        {
            var cacheKey = $"project-stats:{projectId}:hourly-rate";
            if (_cache.TryGetValue(cacheKey, out decimal cached) && cached > 0)
                return cached;

            try
            {
                // Get allocated resource profiles for this project
                var allocatedUserIds = await _db.ProjectResourceAllocations
                    .Where(a => a.ProjectId == projectId)
                    .Select(a => a.UserId)
                    .Distinct()
                    .ToListAsync();

                if (!allocatedUserIds.Any()) return 800m;

                // Use the pre-computed EffectiveHourlyRate from the current active SalaryHistory record
                var rates = await _db.SalaryHistories
                    .Where(s => s.ResourceProfile != null
                        && allocatedUserIds.Contains(s.ResourceProfile.UserId)
                        && s.EffectiveTo == null) // Only the current (active) salary record
                    .Select(s => s.EffectiveHourlyRate)
                    .ToListAsync();

                var hourlyRate = rates.Any() ? (decimal)rates.Average() : 800m;
                _cache.Set(cacheKey, hourlyRate, TimeSpan.FromMinutes(15));
                return hourlyRate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving hourly rate for project {ProjectId}", projectId);
                return 800m; // Fallback BDT hourly rate
            }
        }
    }
}
