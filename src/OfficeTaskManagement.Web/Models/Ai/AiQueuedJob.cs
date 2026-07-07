using System;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Represents an AI job (chat or estimation) that failed and is queued for later retry/resumption.
    /// Persistent store is managed via a JSON file to avoid database migrations.
    /// </summary>
    public class AiQueuedJob : IMustHaveTenant
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        
        /// <summary>"Chat" | "Estimation" | "ReEstimation" | "AcceptanceCriteria"</summary>
        public string JobType { get; set; } = string.Empty;
        
        /// <summary>The serialized C# request DTO so the job can be re-run with original arguments.</summary>
        public string RequestPayloadJson { get; set; } = "{}";
        
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        
        // Context properties for filtering by project/context
        public int? ProjectId { get; set; }
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
    }
}
