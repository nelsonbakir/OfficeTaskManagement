using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Services
{
    /// <summary>
    /// Implementation of <see cref="IBudgetService"/>.
    /// Covers PMBOK Cost Management processes (7.1 Plan, 7.3 Determine Budget, 7.4 Control Costs).
    ///
    /// Advisory thresholds:
    ///   &lt; 80 % utilisation  → Info (green)
    ///   80 %–100 % utilisation → Warning (amber)
    ///   &gt; 100 % utilisation → Critical (red)
    /// </summary>
    public class BudgetService : IBudgetService
    {
        private readonly ApplicationDbContext _db;
        private readonly IResourceService _resourceService;

        // Advisory thresholds (could be moved to appsettings.json later)
        private const decimal WarningThresholdPercent  = 80m;
        private const decimal CriticalThresholdPercent = 100m;

        public BudgetService(ApplicationDbContext db, IResourceService resourceService)
        {
            _db = db;
            _resourceService = resourceService;
        }

        // ── Budget Baseline ───────────────────────────────────────────────────

        public async Task SetProjectBudgetAsync(
            int projectId,
            BudgetMode mode,
            decimal? approvedBudget,
            decimal? contingencyReserve,
            string? setById)
        {
            var project = await _db.Projects.FindAsync(projectId)
                ?? throw new InvalidOperationException($"Project {projectId} not found.");

            project.BudgetMode         = mode;
            project.ApprovedBudget     = (mode == BudgetMode.DerivedFromWork) ? null : approvedBudget;
            project.ContingencyReserve = contingencyReserve;
            project.BudgetSetAt        = DateTime.UtcNow;
            project.BudgetSetById      = setById;

            _db.Projects.Update(project);
            await _db.SaveChangesAsync();
        }

        public async Task<ProjectBudgetSummary> GetBudgetSummaryAsync(int projectId)
        {
            var project = await _db.Projects
                .Include(p => p.BudgetSetBy)
                .FirstOrDefaultAsync(p => p.Id == projectId)
                ?? throw new InvalidOperationException($"Project {projectId} not found.");

            var laborAllocated  = await GetLaborCostAllocatedAsync(projectId);
            var laborEstimated  = await GetLaborCostEstimateAsync(projectId);
            var otherCosts      = await _db.ProjectOtherCosts
                .Where(oc => oc.ProjectId == projectId)
                .ToListAsync();

            var otherEstimated  = otherCosts.Where(oc => !oc.IsContingency).Sum(oc => oc.EstimatedAmount);
            var otherActual     = otherCosts.Where(oc => oc.ActualAmount.HasValue).Sum(oc => oc.ActualAmount!.Value);

            var summary = new ProjectBudgetSummary
            {
                ProjectId              = project.Id,
                ProjectName            = project.Name,
                BudgetMode             = project.BudgetMode,
                ApprovedBudget         = project.ApprovedBudget,
                ContingencyReserve     = project.ContingencyReserve,
                BudgetSetAt            = project.BudgetSetAt,
                BudgetSetByName        = project.BudgetSetBy?.FullName ?? project.BudgetSetBy?.UserName,
                LaborCostAllocated     = laborAllocated,
                LaborCostEstimated     = laborEstimated,
                OtherCostEstimated     = otherEstimated,
                OtherCostActual        = otherActual,
                OtherCostLineItemCount = otherCosts.Count
            };

            summary.HealthStatus = DetermineHealthStatus(summary);
            summary.HealthReason = BuildHealthReason(summary);

            return summary;
        }

        // ── Cost Forecasting ──────────────────────────────────────────────────

        public async Task<decimal> GetLaborCostEstimateAsync(int projectId)
        {
            // Bottom-up: task PERT hours × resource effective hourly rate
            var tasks = await _db.Tasks
                .Where(t => t.ProjectId == projectId
                         && t.AssigneeId != null
                         && (t.PertEstimatedHours > 0 || t.EstimatedHours > 0))
                .Select(t => new
                {
                    Hours      = t.PertEstimatedHours ?? t.EstimatedHours,
                    AssigneeId = t.AssigneeId!
                })
                .ToListAsync();

            if (!tasks.Any()) return 0m;

            var today = DateTime.UtcNow;
            decimal total = 0m;

            // Group by assignee to avoid repeated DB calls
            var assigneeGroups = tasks.GroupBy(t => t.AssigneeId);
            foreach (var group in assigneeGroups)
            {
                var profile = await _db.ResourceProfiles
                    .FirstOrDefaultAsync(rp => rp.UserId == group.Key);
                if (profile == null) continue;

                var rate = await _resourceService.GetEffectiveHourlyRateAsync(profile.Id, today);
                total += group.Sum(t => t.Hours) * rate;
            }

            return Math.Round(total, 2);
        }

        public async Task<decimal> GetLaborCostAllocatedAsync(int projectId)
        {
            // Strategic (PV): allocation hours × resource hourly rate
            var allocations = await _db.ProjectResourceAllocations
                .Include(a => a.ResourceProfile)
                .Where(a => a.ProjectId == projectId && a.ResourceProfileId != null)
                .ToListAsync();

            if (!allocations.Any()) return 0m;

            var today = DateTime.UtcNow;
            decimal total = 0m;

            foreach (var alloc in allocations)
            {
                if (alloc.ResourceProfile == null) continue;

                var rate = await _resourceService.GetEffectiveHourlyRateAsync(
                    alloc.ResourceProfile.Id, today);

                // Estimate working hours: (alloc% / 100) × dailyHours × 22 working days/month × months
                var start    = alloc.StartDate > today ? alloc.StartDate : today;
                var end      = alloc.EndDate ?? start.AddMonths(3); // default 3-month window
                var months   = Math.Max(1, (decimal)(end - start).TotalDays / 30);
                var hours    = (alloc.AllocationPercentage / 100m)
                             * alloc.ResourceProfile.DailyCapacityHours
                             * 22m    // standard working days/month
                             * months;

                total += hours * rate;
            }

            return Math.Round(total, 2);
        }

        public async Task<DerivedBudgetForecast> GetDerivedBudgetForecastAsync(int projectId)
        {
            var laborEstimate = await GetLaborCostEstimateAsync(projectId);
            var otherEstimate = await _db.ProjectOtherCosts
                .Where(oc => oc.ProjectId == projectId && !oc.IsContingency)
                .SumAsync(oc => oc.EstimatedAmount);

            return new DerivedBudgetForecast
            {
                ProjectId         = projectId,
                LaborEstimate     = laborEstimate,
                OtherCostEstimate = otherEstimate,
                ComputedAt        = DateTime.UtcNow
            };
        }

        // ── Advisory Intelligence ─────────────────────────────────────────────

        public async Task<BudgetAdvisory> GetBudgetAdvisoryAsync(int projectId, decimal proposedAdditionalCost)
        {
            var project = await _db.Projects.FindAsync(projectId);
            if (project == null || project.BudgetMode == BudgetMode.NotSet || !project.ApprovedBudget.HasValue)
            {
                return new BudgetAdvisory
                {
                    HasBudget            = false,
                    Level                = BudgetAdvisoryLevel.None,
                    Message              = "No approved budget has been set for this project."
                };
            }

            var summary = await GetBudgetSummaryAsync(projectId);
            var currentTotal = summary.TotalEstimatedCost;
            var projected    = currentTotal + proposedAdditionalCost;
            var budget       = project.ApprovedBudget.Value;
            var utilPct      = budget > 0 ? Math.Round((projected / budget) * 100, 1) : 0m;

            BudgetAdvisoryLevel level;
            string message;

            if (utilPct >= CriticalThresholdPercent)
            {
                level   = BudgetAdvisoryLevel.Critical;
                var over = Math.Round(projected - budget, 2);
                message = $"Over budget — this allocation will push the projected total to BDT {projected:N0} " +
                          $"({utilPct:N1}% of budget), exceeding the approved budget by BDT {over:N0}. " +
                          "Allocation will still be saved.";
            }
            else if (utilPct >= WarningThresholdPercent)
            {
                level   = BudgetAdvisoryLevel.Warning;
                message = $"Caution — this allocation brings projected cost to BDT {projected:N0} " +
                          $"({utilPct:N1}% of approved budget BDT {budget:N0}).";
            }
            else
            {
                level   = BudgetAdvisoryLevel.Info;
                message = $"Budget comfortable — projected spend will be BDT {projected:N0} " +
                          $"({utilPct:N1}% of approved budget BDT {budget:N0}).";
            }

            return new BudgetAdvisory
            {
                HasBudget              = true,
                ApprovedBudget         = budget,
                CurrentTotalEstimate   = currentTotal,
                ProposedAdditionalCost = proposedAdditionalCost,
                Level                  = level,
                Message                = message
            };
        }

        // ── Other Costs CRUD ──────────────────────────────────────────────────

        public async Task<IEnumerable<ProjectOtherCost>> GetOtherCostsAsync(int projectId)
        {
            return await _db.ProjectOtherCosts
                .Where(oc => oc.ProjectId == projectId)
                .OrderByDescending(oc => oc.CreatedAt)
                .ToListAsync();
        }

        public async Task<ProjectOtherCost> AddOtherCostAsync(OtherCostUpsertDto dto, string? createdById)
        {
            var cost = new ProjectOtherCost
            {
                ProjectId       = dto.ProjectId,
                Description     = dto.Description,
                Category        = dto.Category,
                Frequency       = dto.Frequency,
                EstimatedAmount = dto.EstimatedAmount,
                ActualAmount    = dto.ActualAmount,
                PlannedDate     = dto.PlannedDate,
                ActualDate      = dto.ActualDate,
                IsContingency   = dto.IsContingency,
                Notes           = dto.Notes,
                CreatedById     = createdById,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow
            };

            _db.ProjectOtherCosts.Add(cost);
            await _db.SaveChangesAsync();
            return cost;
        }

        public async Task<ProjectOtherCost> UpdateOtherCostAsync(int costId, OtherCostUpsertDto dto)
        {
            var cost = await _db.ProjectOtherCosts.FindAsync(costId)
                ?? throw new InvalidOperationException($"Cost item {costId} not found.");

            cost.Description     = dto.Description;
            cost.Category        = dto.Category;
            cost.Frequency       = dto.Frequency;
            cost.EstimatedAmount = dto.EstimatedAmount;
            cost.ActualAmount    = dto.ActualAmount;
            cost.PlannedDate     = dto.PlannedDate;
            cost.ActualDate      = dto.ActualDate;
            cost.IsContingency   = dto.IsContingency;
            cost.Notes           = dto.Notes;
            cost.UpdatedAt       = DateTime.UtcNow;

            _db.ProjectOtherCosts.Update(cost);
            await _db.SaveChangesAsync();
            return cost;
        }

        public async Task DeleteOtherCostAsync(int costId)
        {
            var cost = await _db.ProjectOtherCosts.FindAsync(costId)
                ?? throw new InvalidOperationException($"Cost item {costId} not found.");

            _db.ProjectOtherCosts.Remove(cost);
            await _db.SaveChangesAsync();
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private static BudgetHealthStatus DetermineHealthStatus(ProjectBudgetSummary s)
        {
            if (!s.ApprovedBudget.HasValue || s.ApprovedBudget <= 0)
                return BudgetHealthStatus.NotSet;

            var pct = s.BudgetUtilizationPercent ?? 0;
            if (pct >= CriticalThresholdPercent) return BudgetHealthStatus.Exceeded;
            if (pct >= WarningThresholdPercent)  return BudgetHealthStatus.AtRisk;
            return BudgetHealthStatus.OnTrack;
        }

        private static string BuildHealthReason(ProjectBudgetSummary s)
        {
            return s.HealthStatus switch
            {
                BudgetHealthStatus.NotSet    => "No approved budget is set for this project.",
                BudgetHealthStatus.OnTrack   => $"Estimated total BDT {s.TotalEstimatedCost:N0} is {s.BudgetUtilizationPercent:N1}% of the approved budget.",
                BudgetHealthStatus.AtRisk    => $"Estimated total BDT {s.TotalEstimatedCost:N0} is approaching the approved budget ({s.BudgetUtilizationPercent:N1}%).",
                BudgetHealthStatus.Exceeded  => $"Estimated total BDT {s.TotalEstimatedCost:N0} exceeds the approved budget of BDT {s.ApprovedBudget:N0} by BDT {Math.Abs(s.BudgetVariance!.Value):N0}.",
                _                            => string.Empty
            };
        }
    }
}
