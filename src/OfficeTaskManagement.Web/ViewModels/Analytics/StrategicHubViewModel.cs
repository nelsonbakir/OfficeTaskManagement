using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.ViewModels.Analytics
{
    public class StrategicHubViewModel
    {
        // ── Org-wide Capacity ────────────────────────────────────────────────
        public OrgCapacity CapacitySnapshot { get; set; } = new();

        /// <summary>Plain-English recommendation string.</summary>
        public string NewProjectRecommendation { get; set; } = string.Empty;

        /// <summary>true if current capacity allows starting a new project.</summary>
        public bool CanStartNewProject { get; set; }

        // ── Portfolio Intelligence ────────────────────────────────────────────
        public List<ProjectHealthCard> ProjectHealthCards { get; set; } = new();

        // ── Team Engagement ──────────────────────────────────────────────────
        public List<EngagementScorecard> MemberScorecards { get; set; } = new();

        // ── Resource Demand Forecast ─────────────────────────────────────────
        public List<ResourceDemandForecast> DemandForecasts { get; set; } = new();

        // ── System Recommendations ───────────────────────────────────────────
        public List<SuggestedReallocation> SuggestedReallocations { get; set; } = new();

        // ── Decision Audit Trail ─────────────────────────────────────────────
        public List<PortfolioDecision> RecentDecisions { get; set; } = new();

        // ── Paused Items ─────────────────────────────────────────────────────
        public List<PausedTaskCard> PausedTasks { get; set; } = new();

        // ── Dropdowns for action modals ──────────────────────────────────────
        public List<SelectListItem> AllUsers { get; set; } = new();
        public List<SelectListItem> AllProjects { get; set; } = new();
        public List<SelectListItem> AllActiveSprints { get; set; } = new();
        public List<SelectListItem> ActiveTasksForReassign { get; set; } = new();
    }

    // ── Supporting types ─────────────────────────────────────────────────────

    public class OrgCapacity
    {
        public decimal CommittedHours { get; set; }
        public decimal AvailableHours { get; set; }
        public decimal UtilizationPercent => AvailableHours > 0
            ? Math.Min(CommittedHours / AvailableHours * 100, 200)
            : 0;
        public CapacityStatus Status { get; set; }
        public int TeamSize { get; set; }
        public int ActiveProjects { get; set; }
    }

    public enum CapacityStatus
    {
        Free,       // < 50% utilisation
        Balanced,   // 50–80%
        AtRisk,     // 80–100%
        Overloaded  // > 100%
    }

    public class ProjectHealthCard
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public ProjectStrategicStatus StrategicStatus { get; set; }
        public decimal CompletionPercent { get; set; }
        public decimal OnTimeRate { get; set; }           // % of tasks completed before due date
        public decimal TeamEngagement { get; set; }       // % of team members active this week
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int OverdueTasks { get; set; }
        public int TeamSize { get; set; }

        /// <summary>0–100 composite health score.</summary>
        public decimal HealthScore { get; set; }

        /// <summary>Green / Amber / Red</summary>
        public string RagStatus => HealthScore >= 70 ? "Green"
            : HealthScore >= 40 ? "Amber"
            : "Red";

        public string? StrategicStatusReason { get; set; }
        public DateTime? StrategicStatusChangedAt { get; set; }
    }

    public class EngagementScorecard
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int TasksClosedThisWeek { get; set; }
        public int TasksClosedLastWeek { get; set; }
        public decimal WeekOnWeekChange => TasksClosedLastWeek > 0
            ? (decimal)(TasksClosedThisWeek - TasksClosedLastWeek) / TasksClosedLastWeek * 100
            : 0;
        public int OverdueTasks { get; set; }
        public decimal CommittedHours { get; set; }
        public string EngagementLevel { get; set; } = "Engaged"; // Idle | Engaged | Overloaded
    }

    public class ResourceDemandForecast
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        /// <summary>Percentage allocation for this week.</summary>
        public decimal Week1Allocation { get; set; }
        
        /// <summary>Percentage allocation for next week.</summary>
        public decimal Week2Allocation { get; set; }
        
        /// <summary>Percentage allocation for in 2 weeks.</summary>
        public decimal Week3Allocation { get; set; }
        
        /// <summary>Percentage allocation for in 3 weeks.</summary>
        public decimal Week4Allocation { get; set; }

        public bool IsGoingRed => Week1Allocation > 100 || Week2Allocation > 100 || Week3Allocation > 100 || Week4Allocation > 100;
        
        public string RedWeek => Week1Allocation > 100 ? "This Week" :
                                 Week2Allocation > 100 ? "Next Week" :
                                 Week3Allocation > 100 ? "In 2 Weeks" :
                                 Week4Allocation > 100 ? "In 3 Weeks" : "";
    }

    public class SuggestedReallocation
    {
        public string UserName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string CurrentEngagementLevel { get; set; } = string.Empty; // "Idle" triggers suggestion
        public string SuggestedProjectName { get; set; } = string.Empty;
        public int SuggestedProjectId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class PausedTaskCard
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = string.Empty;
        public string AssigneeName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string PauseReason { get; set; } = string.Empty;
        public DateTime? PausedAt { get; set; }
        public string PausedByName { get; set; } = string.Empty;
    }
}
