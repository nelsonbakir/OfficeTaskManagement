using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Background service (IHostedService) that runs every 5 minutes and
    /// activates workflow stage sub-tasks whose <see cref="TaskItem.PlannedStartDate"/>
    /// has elapsed (lag scheduling — PMP PDM lag/lead time).
    ///
    /// When a stage's PlannedStartDate is reached:
    ///   1. Its status is promoted from <c>New</c> → <c>ToDo</c>.
    ///   2. A <see cref="TaskHistory"/> audit entry is written.
    ///   3. A <see cref="Notification"/> is pushed to the assignee.
    ///   4. <c>PlannedStartDate</c> is nulled to prevent re-processing.
    ///
    /// Paused stages are skipped — they activate only after being unpaused.
    /// </summary>
    public sealed class LagSchedulingService : BackgroundService
    {
        private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LagSchedulingService> _logger;

        public LagSchedulingService(
            IServiceScopeFactory scopeFactory,
            ILogger<LagSchedulingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[LagScheduler] Service started. Polling every {Interval}.", _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ActivateDueStagesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[LagScheduler] Unhandled error during lag activation cycle.");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("[LagScheduler] Service stopped.");
        }

        private async Task ActivateDueStagesAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            // Find all stage sub-tasks that:
            //  - are still "New" (waiting for lag delay to elapse)
            //  - have a PlannedStartDate that is now in the past
            //  - are NOT paused
            //  - are NOT already active
            var due = await db.Tasks
                .Include(t => t.WorkflowStage)
                .Where(t =>
                    t.WorkflowStageId != null &&
                    t.Status == TaskStatus.New &&
                    t.PlannedStartDate != null &&
                    t.PlannedStartDate <= now &&
                    !t.IsPaused)
                .ToListAsync(ct);

            if (!due.Any())
                return;

            _logger.LogInformation("[LagScheduler] Activating {Count} lag-scheduled stage(s).", due.Count);

            foreach (var stage in due)
            {
                // Skip if parent WP is paused
                if (stage.ParentTaskId.HasValue)
                {
                    var parent = await db.Tasks.FindAsync(new object[] { stage.ParentTaskId.Value }, ct);
                    if (parent is { IsPaused: true })
                    {
                        _logger.LogDebug(
                            "[LagScheduler] Skipping stage {Id} — parent WP {ParentId} is paused.",
                            stage.Id, stage.ParentTaskId);
                        continue;
                    }
                }

                var oldStatus = stage.Status.ToString();
                stage.Status = TaskStatus.ToDo;
                stage.PlannedStartDate = null; // Clear so it doesn't re-trigger

                db.TaskHistories.Add(new TaskHistory
                {
                    TaskItemId        = stage.Id,
                    ChangedById       = null, // System action
                    FieldChanged      = "Status",
                    OldValue          = oldStatus,
                    NewValue          = TaskStatus.ToDo.ToString(),
                    RaciRoleAtTime    = OfficeTaskManagement.Models.Enums.RaciRole.Responsible,
                    ChangeDescription = $"[LagScheduler] Stage '{stage.WorkflowStage?.Name}' lag elapsed — activated automatically at {now:u}.",
                    Timestamp         = now
                });

                // Notify assignee
                if (!string.IsNullOrEmpty(stage.AssigneeId))
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId  = stage.AssigneeId,
                        Title   = "Your Stage Is Now Active",
                        Message = $"Stage '{stage.WorkflowStage?.Name}' has been activated after its scheduled lag delay.",
                        Link    = $"/TaskItems/Edit/{stage.Id}",
                        Type    = "WorkflowActivation"
                    });
                }

                _logger.LogInformation(
                    "[LagScheduler] Stage {Id} ('{Name}') activated.",
                    stage.Id, stage.WorkflowStage?.Name ?? stage.Title);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
