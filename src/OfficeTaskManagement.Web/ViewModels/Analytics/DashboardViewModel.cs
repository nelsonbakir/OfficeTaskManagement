using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OfficeTaskManagement.ViewModels.Analytics
{
    public class DashboardViewModel
    {
        public string UserRole { get; set; } = "Employee";
        
        // Filter selections
        public string? SelectedAssigneeId { get; set; }
        public int? SelectedProjectId { get; set; }
        
        public SelectList? Assignees { get; set; }
        public SelectList? Projects { get; set; }

        // Analytics data
        public List<DailyEngagement> Engagements { get; set; } = new List<DailyEngagement>();
        public List<SprintBurndown> Burndowns { get; set; } = new List<SprintBurndown>();
        public List<SprintVelocity> Velocities { get; set; } = new List<SprintVelocity>();

        // Manager Dashboard Metrics
        public ManagerDashboard? ManagerMetrics { get; set; }
        
        // Project Lead Dashboard Metrics
        public ProjectLeadDashboard? ProjectLeadMetrics { get; set; }
        
        // Coordinator Dashboard Metrics
        public CoordinatorDashboard? CoordinatorMetrics { get; set; }
        
        // Employee Dashboard Metrics
        public EmployeeDashboard? EmployeeMetrics { get; set; }

        /// <summary>When true, the unified engagement/delivery/capacity/cost sections are shown below the role overview.</summary>
        public bool IncludeUnifiedAnalytics { get; set; }

        // Resource Management Analytics
        public List<ResourceUtilizationMetric> TeamUtilization { get; set; } = new();
        public List<BenchResourceMetric> BenchResources { get; set; } = new();
        public List<CostTrackingMetric> ProjectCosts { get; set; } = new();
        public List<SprintCapacityMetric> SprintCapacities { get; set; } = new();
    }

    // Manager Dashboard - Organization-wide metrics
    public class ManagerDashboard
    {
        public int TotalProjects { get; set; }
        public int ActiveProjects { get; set; }
        public int TotalTeamMembers { get; set; }
        public decimal AverageTeamUtilization { get; set; } // 0-100%
        public int OverallTaskCompletion { get; set; } // 0-100%
        public decimal AverageSprintVelocity { get; set; }
        public List<ProjectMetric> ProjectMetrics { get; set; } = new();
        public List<EmployeeMetric> EmployeeMetrics { get; set; } = new();
        public List<SprintMetric> RecentSprints { get; set; } = new();
        public int AtRiskTasks { get; set; }
        public int OverdueTasks { get; set; }
        
        // Advanced Analytics Data
        public Dictionary<string, int> TaskStatusDistribution { get; set; } = new();
        public Dictionary<string, decimal> EmployeeWorkload { get; set; } = new();
        public List<string> TopBlockers { get; set; } = new();
    }

    // Project Lead Dashboard - Project and team specific
    public class ProjectLeadDashboard
    {
        public string ProjectName { get; set; } = "";
        public int ProjectId { get; set; }
        public int TotalSprints { get; set; }
        public int ActiveSprints { get; set; }
        public int CompletedSprints { get; set; }
        public int TeamSize { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public decimal ProjectCompletion { get; set; } // 0-100%
        public List<TeamMemberMetric> TeamMetrics { get; set; } = new();
        public List<SprintMetric> SprintMetrics { get; set; } = new();
        public int BlockedTasks { get; set; }

        // Advanced Analytics Data
        public Dictionary<string, int> TaskStatusDistribution { get; set; } = new();
        public Dictionary<string, decimal> EmployeeWorkload { get; set; } = new();
    }

    // Coordinator Dashboard - Operational metrics
    public class CoordinatorDashboard
    {
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int ToDoTasks { get; set; }
        public int UnassignedTasks { get; set; }
        public decimal TaskCompletionRate { get; set; }
        public int CurrentSprints { get; set; }
        public List<SprintProgressMetric> SprintProgress { get; set; } = new();
        public List<BlockerMetric> Blockers { get; set; } = new();
        public List<TaskMetric> UpcomingDeadlines { get; set; } = new();
        public int OverdueTasks { get; set; }
    }

    // Employee Dashboard - Personal metrics
    public class EmployeeDashboard
    {
        public string EmployeeName { get; set; } = "";
        public int AssignedTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int ToDoTasks { get; set; }
        public decimal TaskCompletion { get; set; } // 0-100%
        public decimal CurrentWeekload { get; set; }
        public int CurrentSprints { get; set; }
        public List<PersonalTaskMetric> MyTasks { get; set; } = new();
        public List<string> MyProjects { get; set; } = new();
    }

    // Supporting metric classes
    public class ProjectMetric
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal Completion { get; set; }
        public string Status { get; set; } = "On Track";
        public int TeamSize { get; set; }
    }

    public class EmployeeMetric
    {
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public int AssignedTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal Utilization { get; set; }
        public decimal Productivity { get; set; }
    }

    public class TeamMemberMetric
    {
        public string MemberId { get; set; } = "";
        public string MemberName { get; set; } = "";
        public int AssignedTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal Utilization { get; set; }
        public string Status { get; set; } = "Active";
    }

    public class SprintMetric
    {
        public int SprintId { get; set; }
        public string SprintName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal Velocity { get; set; }
        public decimal Completion { get; set; }
        public string Status { get; set; } = "On Track";
    }

    public class SprintProgressMetric
    {
        public int SprintId { get; set; }
        public string SprintName { get; set; } = "";
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal Progress { get; set; }
        public DateTime EndDate { get; set; }
        public int DaysRemaining { get; set; }
    }

    public class BlockerMetric
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = "";
        public string BlockedReason { get; set; } = "";
        public string AssigneeName { get; set; } = "";
        public DateTime CreatedDate { get; set; }
    }

    public class TaskMetric
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = "";
        public DateTime DueDate { get; set; }
        public string AssigneeName { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class PersonalTaskMetric
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string SprintName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? DueDate { get; set; }
        public decimal EstimatedHours { get; set; }
    }

    // Legacy classes (keep for backward compatibility)
    public class DailyEngagement
    {
        public string AssigneeName { get; set; } = string.Empty;
        public string TaskTitle { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }
        public bool IsToDo { get; set; }
    }

    public class SprintBurndown
    {
        public string SprintName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal RemainingHours { get; set; }
    }

    public class SprintVelocity
    {
        public string SprintName { get; set; } = string.Empty;
        public decimal CompletedHours { get; set; }
    }

    // Resource Management Analytics Classes
    public class ResourceUtilizationMetric
    {
        public string UserName { get; set; } = string.Empty;
        public decimal UtilizationPercentage { get; set; }
        public decimal AllocatedHours { get; set; }
        public decimal CapacityHours { get; set; }
    }

    public class CostTrackingMetric
    {
        public string ProjectName { get; set; } = string.Empty;
        
        /// <summary>Cost based on high-level resource allocations (PV).</summary>
        public decimal PlannedValuePV { get; set; }
        
        /// <summary>Cost based on individual task estimates (Bottom-up EAC).</summary>
        public decimal BottomUpEstimateEAC { get; set; }
        
        public decimal TotalTaskHours { get; set; }
        
        public decimal CostVariance => PlannedValuePV - BottomUpEstimateEAC;

        [Obsolete("Use PlannedValuePV")]
        public decimal EstimatedCost => PlannedValuePV;
    }

    public class BenchResourceMetric
    {
        public string UserName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal CurrentUtilization { get; set; }
    }

    public class SprintCapacityMetric
    {
        public string SprintName { get; set; } = string.Empty;
        public decimal PlannedCapacity { get; set; }
        public decimal TaskDemand { get; set; }
        public decimal Delta { get; set; }
    }
}
