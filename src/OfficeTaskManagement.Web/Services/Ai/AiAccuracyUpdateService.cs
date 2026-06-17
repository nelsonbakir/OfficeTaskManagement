using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Enums;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Services.Ai;

/// <summary>
/// Background service (T52): daily job that finds completed tasks whose
/// AI estimation log still has ActualHours == null, and back-fills
/// the actual hours so the AI accuracy dashboard has real data.
/// Spec: ai-agent-plan/10_EXECUTION_TASKS.md → T52
/// </summary>
public class AiAccuracyUpdateService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiAccuracyUpdateService> _logger;

    // Run once at startup (after 30 s delay) then every 24 hours.
    private static readonly TimeSpan InitialDelay  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunInterval   = TimeSpan.FromHours(24);

    public AiAccuracyUpdateService(
        IServiceScopeFactory scopeFactory,
        ILogger<AiAccuracyUpdateService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AiAccuracyUpdateService starting. Initial delay: {Delay}s", InitialDelay.TotalSeconds);
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateAccuracyRecordsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AiAccuracyUpdateService: unhandled error during update cycle.");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    private async Task UpdateAccuracyRecordsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Find AI logs for Tasks that are Done but missing actual hours
        var logsToUpdate = await db.AiEstimationLogs
            .Where(l => l.EntityType == "Task"
                     && l.EntityId.HasValue
                     && l.ActualHours == null)
            .ToListAsync(ct);

        if (logsToUpdate.Count == 0)
        {
            _logger.LogDebug("AiAccuracyUpdateService: no records to update.");
            return;
        }

        var entityIds = logsToUpdate.Select(l => l.EntityId!.Value).Distinct().ToList();

        // Load completed tasks with actual hours in one query
        var completedTasks = await db.Tasks
            .Where(t => entityIds.Contains(t.Id)
                     && t.Status == TaskStatus.Done
                     && t.ActualHours.HasValue)
            .Select(t => new { t.Id, t.ActualHours })
            .ToDictionaryAsync(t => t.Id, t => t.ActualHours!.Value, ct);

        int updated = 0;
        foreach (var log in logsToUpdate)
        {
            if (log.EntityId.HasValue && completedTasks.TryGetValue(log.EntityId.Value, out var actual))
            {
                log.ActualHours = actual;
                updated++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("AiAccuracyUpdateService: updated {Count} AI log records with actual hours.", updated);
        }
    }
}
