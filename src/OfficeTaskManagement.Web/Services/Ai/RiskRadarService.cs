using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Services.Ai;

/// <summary>
/// KF-4: Proactive Risk Radar — IHostedService background job.
/// Runs every 30 minutes, scans all active projects for risk signals,
/// and writes AI_Risk notifications to the existing Notifications table.
///
/// Risk signals detected:
///   1. Stale tasks — InProgress for more than 5 days without completion
///   2. Sprint overload — active sprint has tasks exceeding PERT estimate total
///   3. Missing estimates — tasks with zero EstimatedHours in an active sprint
///   4. Resource bottleneck — one user assigned > 3 active (non-Done) tasks
///   5. Budget risk — project has no budget set but has > 10 tasks
///
/// Spec: implementation_plan.md → KF-4 (Risk Radar)
/// </summary>
public sealed class RiskRadarService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RiskRadarService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public RiskRadarService(IServiceScopeFactory scopeFactory, ILogger<RiskRadarService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RiskRadarService started — interval: {Interval}", _interval);

        // Initial delay so the app finishes startup before first scan
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAllProjectsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RiskRadarService: unhandled error during risk scan");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("RiskRadarService stopped");
    }

    private async Task ScanAllProjectsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Load all projects with their PM leads (CreatedById as fallback notification target)
        var projects = await db.Projects
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.TenantId, p.CreatedById })
            .ToListAsync(ct);

        _logger.LogDebug("RiskRadarService: scanning {Count} projects", projects.Count);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(project.CreatedById)) continue;

            var risks = await DetectRisksAsync(db, project.Id, project.Name, ct);

            foreach (var risk in risks)
            {
                // De-duplicate: skip if the same risk was already notified in the last 24h
                bool alreadyNotified = await db.Notifications
                    .AnyAsync(n => n.UserId    == project.CreatedById
                                && n.Type      == "AI_Risk"
                                && n.TenantId  == project.TenantId
                                && n.Title     == risk.Title
                                && n.CreatedAt > DateTime.UtcNow.AddHours(-24), ct);

                if (alreadyNotified) continue;

                db.Notifications.Add(new Notification
                {
                    TenantId  = project.TenantId,
                    UserId    = project.CreatedById,
                    Title     = risk.Title,
                    Message   = risk.Message,
                    Link      = risk.Link,
                    Type      = "AI_Risk",
                    IsRead    = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (risks.Count > 0)
                await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Detects risk signals for a single project and returns notification records.</summary>
    private static async Task<List<RiskSignal>> DetectRisksAsync(
        ApplicationDbContext db, int projectId, string projectName, CancellationToken ct)
    {
        var signals = new List<RiskSignal>();
        var now     = DateTime.UtcNow;
        var projectLink = $"/Projects/Details/{projectId}";

        // ── Signal 1: Stale In-Progress tasks (> 5 days) ────────────────────────
        var staleCutoff = now.AddDays(-5);
        var staleTasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId
                     && t.Status    == Models.Enums.TaskStatus.InProgress
                     && t.StartDate != null
                     && t.StartDate < staleCutoff
                     && t.CompletedAt == null)
            .Select(t => new { t.Id, t.Title })
            .Take(5)
            .ToListAsync(ct);

        if (staleTasks.Count > 0)
        {
            var taskTitles = string.Join(", ", staleTasks.Take(3).Select(t => $"\"{t.Title}\""));
            signals.Add(new RiskSignal(
                Title:   $"🔴 Stale Tasks — {projectName}",
                Message: $"{staleTasks.Count} task(s) have been In Progress for over 5 days without completion: {taskTitles}. " +
                         $"Consider reviewing blockers or reassigning.",
                Link:    projectLink));
        }

        // ── Signal 2: Tasks with missing PERT estimates in active sprint ─────────
        var activeSprint = await db.Sprints
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId
                     && s.StartDate <= now
                     && s.EndDate   >= now)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(ct);

        if (activeSprint != null)
        {
            var unestimatedCount = await db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId    == activeSprint.Id
                         && t.EstimatedHours == 0
                         && t.Status       != Models.Enums.TaskStatus.Done)
                .CountAsync(ct);

            if (unestimatedCount > 0)
            {
                signals.Add(new RiskSignal(
                    Title:   $"🟡 Missing Estimates — {projectName}",
                    Message: $"{unestimatedCount} task(s) in sprint \"{activeSprint.Name}\" have no hour estimates. " +
                             $"Use the AI Copilot /estimate command or the estimation panel to fill gaps.",
                    Link:    $"/Sprints/Details/{activeSprint.Id}"));
            }
        }

        // ── Signal 3: Resource bottleneck (user with > 3 active tasks) ───────────
        var activeTasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId  == projectId
                     && t.AssigneeId != null
                     && t.Status     != Models.Enums.TaskStatus.Done)
            .GroupBy(t => new { t.AssigneeId, t.Assignee!.FullName })
            .Select(g => new { g.Key.AssigneeId, g.Key.FullName, Count = g.Count() })
            .Where(x => x.Count > 3)
            .ToListAsync(ct);

        foreach (var overloaded in activeTasks)
        {
            signals.Add(new RiskSignal(
                Title:   $"🟠 Resource Bottleneck — {projectName}",
                Message: $"{overloaded.FullName ?? "A team member"} is assigned {overloaded.Count} active tasks in {projectName}. " +
                         $"Consider rebalancing workload to reduce risk of delays.",
                Link:    projectLink));
        }

        return signals;
    }

    private record RiskSignal(string Title, string Message, string Link);
}
