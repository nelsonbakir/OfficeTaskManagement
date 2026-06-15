using System;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Persists multi-turn conversation history for the AI Copilot sidebar.
    /// Each conversation is scoped to a user + a PM entity (e.g., an Epic).
    /// Implements IMustHaveTenant for multi-tenant query filtering.
    /// </summary>
    public class AgentConversation : IMustHaveTenant
    {
        [Key]
        [StringLength(36)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        public string TenantId { get; set; } = string.Empty;

        /// <summary>PM entity context: "Epic" | "Feature" | "UserStory" | "Task" | "Project"</summary>
        [StringLength(50)]
        public string? EntityType { get; set; }

        /// <summary>The ID of the PM entity this conversation is about.</summary>
        public int? EntityId { get; set; }

        /// <summary>
        /// JSON array of conversation turns.
        /// Schema: [{ "role": "user"|"model", "text": "...", "timestamp": "..." }]
        /// Stored as JSONB in PostgreSQL.
        /// </summary>
        [Required]
        public string TurnsJson { get; set; } = "[]";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Conversations expire after 24h of inactivity to save storage.</summary>
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    }
}
