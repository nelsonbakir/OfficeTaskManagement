using System;
using System.Collections.Generic;

namespace OfficeTaskManagement.Models.Ai
{
    // ── Step 1 Output: AI-proposed sprint goal ────────────────────────────────

    /// <summary>
    /// AI-generated sprint goal proposal, produced by analysing the project backlog
    /// and proposing a coherent, achievable sprint goal for the given date window.
    /// </summary>
    public record SprintGoalProposalDto(
        string GoalStatement,
        string[] KeyThemes,
        string RiskSummary,
        string RecommendedSprintName
    );

    // ── Step 2 Output: Capacity gate — per-resource availability ─────────────

    /// <summary>
    /// Availability slot for a single team member during the proposed sprint window.
    /// Computed from ResourceProfile, ProjectResourceAllocation, and availability blocks.
    /// </summary>
    public record ResourceCapacitySlotDto(
        string UserId,
        string FullName,
        string? AvatarPath,
        string? Role,
        decimal AvailableHours,
        decimal AllocatedHours,
        decimal CurrentLoadPct,
        string[] Skills
    );

    /// <summary>
    /// Aggregated team capacity response for Step 2 of the wizard.
    /// </summary>
    public class SprintCapacityGateDto
    {
        public decimal TotalAvailableHours { get; set; }
        public decimal TotalAllocatedHours { get; set; }
        public bool IsTeamOverAllocated { get; set; }
        public List<ResourceCapacitySlotDto> Resources { get; set; } = new();
    }

    // ── Step 3 Output: AI-selected + PERT-sized backlog tasks ────────────────

    /// <summary>
    /// A single task suggested by the AI for inclusion in the sprint.
    /// <see cref="TaskId"/> is null for tasks that must be created; non-null for existing backlog tasks.
    /// </summary>
    public record SprintTaskSuggestionDto(
        int? TaskId,
        string Title,
        string? Description,
        string Priority,
        decimal PertHours,
        decimal OptimisticHours,
        decimal MostLikelyHours,
        decimal PessimisticHours,
        int StoryPoints,
        string Rationale,
        bool IsNewTask,
        bool Selected
    );

    /// <summary>
    /// AI backlog-selection response for Step 3.
    /// </summary>
    public class SprintBacklogSelectionDto
    {
        public List<SprintTaskSuggestionDto> SuggestedTasks { get; set; } = new();
        public decimal TotalSelectedHours { get; set; }
        public decimal CapacityHours { get; set; }
        public decimal UtilizationPct { get; set; }
        public string SelectionRationale { get; set; } = string.Empty;
    }

    // ── Step 4 Output: Task-to-resource assignment suggestions ───────────────

    /// <summary>
    /// AI-suggested assignment for a single sprint task.
    /// </summary>
    public record TaskAssignmentSuggestionDto(
        int? TaskId,
        string Title,
        string? SuggestedAssigneeId,
        string? SuggestedAssigneeName,
        string? SuggestedAssigneeAvatarPath,
        string AssignmentReason,
        decimal TaskPertHours,
        string Priority
    );

    // ── Step 5 Input: User-confirmed sprint plan (sent to /api/sprint-planner/confirm) ──

    /// <summary>
    /// Final confirmation payload sent from the wizard's Step 5 review.
    /// Persisted in a single DB transaction when the user clicks "Confirm &amp; Save".
    /// </summary>
    public class ConfirmSprintPlanRequest
    {
        public int ProjectId { get; set; }
        public SprintPlanDto Sprint { get; set; } = new();
    }

    public class SprintPlanDto
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Goal { get; set; } = string.Empty;
        public decimal PlannedCapacityHours { get; set; }
        public List<SprintTaskConfirmDto> Tasks { get; set; } = new();
    }

    public class SprintTaskConfirmDto
    {
        /// <summary>Null for new tasks to be created.</summary>
        public int? TaskId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Priority { get; set; } = "Medium";
        public decimal OptimisticHours { get; set; }
        public decimal MostLikelyHours { get; set; }
        public decimal PessimisticHours { get; set; }
        public decimal EstimatedHours { get; set; }
        public string? AssigneeId { get; set; }
        public bool IsNewTask { get; set; }
    }

    /// <summary>
    /// Response from the confirm endpoint — contains the new sprint ID for redirect.
    /// </summary>
    public class ConfirmSprintPlanResponse
    {
        public int SprintId { get; set; }
        public string SprintName { get; set; } = string.Empty;
        public int TasksCreated { get; set; }
        public int TasksAssigned { get; set; }
    }

    // ── API request bodies ────────────────────────────────────────────────────

    public class ProposeSprintGoalRequest
    {
        public int ProjectId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class SelectSprintBacklogRequest
    {
        public int ProjectId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalCapacityHours { get; set; }
    }

    public class AssignSprintTasksRequest
    {
        public int ProjectId { get; set; }
        public List<SprintTaskSuggestionDto> Tasks { get; set; } = new();
        public List<ResourceCapacitySlotDto> Resources { get; set; } = new();
    }
}
