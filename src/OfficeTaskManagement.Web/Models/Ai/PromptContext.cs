using System.Collections.Generic;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Compressed context snapshot passed to GeminiAiService for each estimation call.
    /// Built by ContextBuilderService following the token budget allocation rules
    /// defined in 03_PROMPT_STRATEGY.md.
    /// </summary>
    public class PromptContext
    {
        /// <summary>
        /// Parent entity name + description (1 level up).
        /// Budget: ~400 tokens.
        /// </summary>
        public string? ParentContext { get; set; }

        /// <summary>
        /// Names of existing sibling entities (comma-separated, no descriptions).
        /// Budget: ~600 tokens.
        /// </summary>
        public string? SiblingList { get; set; }

        /// <summary>
        /// Compressed historical accuracy stats for this project + entity type.
        /// Budget: ~500 tokens.
        /// Format: "Backend tasks: avg 8h est → 11h actual (38% overrun)\n..."
        /// </summary>
        public string? HistoryStats { get; set; }

        /// <summary>
        /// Average hourly rate in BDT for the project's allocated team members.
        /// Sourced from SalaryHistory → ProjectResourceAllocations.
        /// Fallback: 800 BDT/hr.
        /// </summary>
        public decimal HourlyRateBDT { get; set; } = 800m;

        /// <summary>
        /// Top-K relevant code chunks from the codebase RAG pipeline (Phase 3+).
        /// Budget: ~1,500 tokens. Null when Phase 3 RAG is not yet available.
        /// </summary>
        public IReadOnlyList<string>? CodeChunks { get; set; }
    }
}
