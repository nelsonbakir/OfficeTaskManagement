using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services.Onboarding
{
    /// <summary>
    /// Persists the wizard step checkpoint for each project so users can resume
    /// after a page refresh without losing progress.
    /// </summary>
    public class OnboardingSessionService
    {
        private readonly ApplicationDbContext _db;

        public OnboardingSessionService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>Loads (or creates) the checkpoint for a project. Never returns null.</summary>
        public async Task<OnboardingCheckpoint> GetOrCreateAsync(int projectId, string tenantId, CancellationToken ct = default)
        {
            var checkpoint = await _db.OnboardingCheckpoints
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (checkpoint != null) return checkpoint;

            checkpoint = new OnboardingCheckpoint
            {
                ProjectId          = projectId,
                TenantId           = tenantId,
                LastCompletedStep  = 0,
                IsCompleted        = false,
                CreatedAt          = DateTime.UtcNow,
                UpdatedAt          = DateTime.UtcNow
            };
            _db.OnboardingCheckpoints.Add(checkpoint);
            await _db.SaveChangesAsync(ct);
            return checkpoint;
        }

        /// <summary>Marks a step as the last-completed and persists it.</summary>
        public async Task MarkStepCompleteAsync(int projectId, int step, CancellationToken ct = default)
        {
            var checkpoint = await _db.OnboardingCheckpoints
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (checkpoint == null) return;

            if (step > checkpoint.LastCompletedStep)
                checkpoint.LastCompletedStep = step;

            checkpoint.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Marks onboarding as fully completed.</summary>
        public async Task MarkCompletedAsync(int projectId, CancellationToken ct = default)
        {
            var checkpoint = await _db.OnboardingCheckpoints
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (checkpoint == null) return;

            checkpoint.IsCompleted       = true;
            checkpoint.LastCompletedStep = 6;
            checkpoint.UpdatedAt         = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
