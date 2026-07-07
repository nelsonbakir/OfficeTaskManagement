using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Service to manage failed AI jobs persisted in the database.
    /// Thread-safe and transactional via EF Core.
    /// </summary>
    public class AiQueuedJobService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AiQueuedJobService> _logger;

        public AiQueuedJobService(ApplicationDbContext db, ILogger<AiQueuedJobService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<AiQueuedJob> AddJobAsync(
            string tenantId,
            string userId,
            string jobType,
            string payloadJson,
            string errorMessage,
            int? projectId = null,
            string? entityType = null,
            int? entityId = null)
        {
            var job = new AiQueuedJob
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                UserId = userId,
                JobType = jobType,
                RequestPayloadJson = payloadJson,
                ErrorMessage = errorMessage,
                ProjectId = projectId,
                EntityType = entityType,
                EntityId = entityId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.AiQueuedJobs.Add(job);
            await _db.SaveChangesAsync();
            return job;
        }

        public async Task<List<AiQueuedJob>> GetJobsAsync(string tenantId, string userId, int? projectId = null)
        {
            var query = _db.AiQueuedJobs.Where(j => j.TenantId == tenantId && j.UserId == userId);
            if (projectId.HasValue)
            {
                query = query.Where(j => j.ProjectId == projectId.Value);
            }
            return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
        }

        public async Task<AiQueuedJob?> GetJobByIdAsync(string jobId)
        {
            return await _db.AiQueuedJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        }

        public async Task<bool> DeleteJobAsync(string jobId)
        {
            var job = await _db.AiQueuedJobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job != null)
            {
                _db.AiQueuedJobs.Remove(job);
                await _db.SaveChangesAsync();
                return true;
            }
            return false;
        }
    }
}
