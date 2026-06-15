using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Inserts audit log records for every AI estimation call.
    /// Tracks token usage for cost monitoring and populates ActualHours
    /// retroactively (via a background job in Phase 5) for accuracy analytics.
    /// </summary>
    public class AiEstimationLogService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AiEstimationLogService> _logger;

        public AiEstimationLogService(
            ApplicationDbContext db,
            ILogger<AiEstimationLogService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Logs a single AI estimation call to the AiEstimationLogs table.
        /// Failures are caught and logged — never propagated to callers.
        /// </summary>
        public async Task LogAsync(
            string entityType,
            int? entityId,
            string userId,
            string tenantId,
            int inputTokens,
            int outputTokens,
            string model,
            decimal? aiPertHours = null)
        {
            try
            {
                _db.AiEstimationLogs.Add(new AiEstimationLog
                {
                    EntityType    = entityType,
                    EntityId      = entityId,
                    UserId        = userId,
                    TenantId      = tenantId,
                    InputTokens   = inputTokens,
                    OutputTokens  = outputTokens,
                    Model         = model,
                    AiPertHours   = aiPertHours,
                    CreatedAt     = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log but never throw — estimation logging should never block the UI
                _logger.LogError(ex,
                    "Failed to write AiEstimationLog for {EntityType}/{EntityId}", entityType, entityId);
            }
        }
    }
}
