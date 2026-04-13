using System;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Infers the most appropriate <see cref="StageGateType"/> from a workflow stage name.
    /// Called automatically when a stage is created so PMs never need to manually set gate types.
    /// The inference covers the full vocabulary of standard SDLC, PMP, and agile stage names.
    ///
    /// Priority order (first match wins, most specific first):
    ///   1. Test/QA/UAT patterns   → TestedWithAllCasesPassed
    ///   2. Review/Audit/Approval  → CommittedWithPeerReview
    ///   3. Build/Implement/Deploy → CommittedWithHours
    ///   4. Completion/Handover    → CommittedOnly
    ///   5. Everything else        → None
    /// </summary>
    public static class StageGateInferenceService
    {
        // ── Pattern banks (lower-case keywords) ────────────────────────────────

        /// <summary>
        /// Triggers TestedWithAllCasesPassed — stages that must demonstrate test evidence.
        /// </summary>
        private static readonly string[] TestPatterns =
        {
            "qa", "quality assurance",
            "test", "testing", "tester",
            "uat", "user acceptance",
            "acceptance test", "acceptance testing",
            "regression", "regression test",
            "smoke test", "smoke testing",
            "integration test", "e2e test", "end-to-end test",
            "verify", "verification"
        };

        /// <summary>
        /// Triggers CommittedWithPeerReview — stages that require a recorded peer opinion.
        /// </summary>
        private static readonly string[] ReviewPatterns =
        {
            "review", "peer review", "code review",
            "audit", "security audit",
            "approval", "approve",
            "inspect", "inspection",
            "validate", "validation",
            "sign off", "signoff", "sign-off",
            "walkthrough", "desk check",
            "assessment", "evaluation"
        };

        /// <summary>
        /// Triggers CommittedWithHours — stages that require logged effort as work evidence.
        /// </summary>
        private static readonly string[] ImplementationPatterns =
        {
            "develop", "development", "developer",
            "implement", "implementation",
            "build", "building",
            "code", "coding",
            "fix", "bug fix", "hotfix",
            "refactor", "refactoring",
            "migrate", "migration", "data migration",
            "setup", "set up", "set-up",
            "configure", "configuration",
            "install", "installation",
            "design", "ui design", "ux design",
            "prototype", "prototyping",
            "deploy", "deployment",
            "release", "release candidate",
            "integration", "integrating"
        };

        /// <summary>
        /// Triggers CommittedOnly — completion or handover stages needing a committed signal only.
        /// </summary>
        private static readonly string[] CompletionPatterns =
        {
            "done", "complete", "completion",
            "document", "documentation", "write docs", "update docs",
            "handover", "hand over", "hand-over",
            "closure", "close", "closing",
            "publish", "publishing",
            "announce", "announcement",
            "training", "knowledge transfer",
            "demo", "demonstration",
            "stage", "staging"
        };

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the inferred <see cref="StageGateType"/> for the given stage name.
        /// Returns <see cref="StageGateType.None"/> when no pattern matches.
        /// This method is deterministic and safe to call multiple times with the same input.
        /// </summary>
        public static StageGateType InferFromName(string stageName)
        {
            if (string.IsNullOrWhiteSpace(stageName))
                return StageGateType.None;

            var normalized = stageName.Trim().ToLowerInvariant();

            // 1 — Test / QA (most strict — check first)
            if (MatchesAny(normalized, TestPatterns))
                return StageGateType.TestedWithAllCasesPassed;

            // 2 — Review / Audit / Approval (peer evidence required)
            if (MatchesAny(normalized, ReviewPatterns))
                return StageGateType.CommittedWithPeerReview;

            // 3 — Implementation / Build / Deploy (work hours required)
            if (MatchesAny(normalized, ImplementationPatterns))
                return StageGateType.CommittedWithHours;

            // 4 — Completion / Handover (committed signal only)
            if (MatchesAny(normalized, CompletionPatterns))
                return StageGateType.CommittedOnly;

            // 5 — No recognisable pattern → no gate
            return StageGateType.None;
        }

        /// <summary>
        /// Returns a short human-readable label explaining the inferred gate.
        /// Displayed in the UI alongside the dropdown so the PM understands what was chosen.
        /// </summary>
        public static string DescribeGateType(StageGateType gate) => gate switch
        {
            StageGateType.None                  => "No gate — stage passes freely.",
            StageGateType.CommittedOnly         => "Requires: Status = Committed.",
            StageGateType.CommittedWithHours    => "Requires: Status = Committed + Actual Hours logged.",
            StageGateType.CommittedWithPeerReview => "Requires: Status = Committed + at least 1 review comment.",
            StageGateType.TestedWithAllCasesPassed=> "Requires: Status = Tested + all Test Cases passed.",
            _ => "Unknown gate type."
        };

        // ── Private Helpers ─────────────────────────────────────────────────────

        private static bool MatchesAny(string normalizedInput, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (normalizedInput.Contains(pattern, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
