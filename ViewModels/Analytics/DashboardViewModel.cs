using System;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.Analytics
{
    public class DashboardViewModel
    {
        public List<DailyEngagement> Engagements { get; set; } = new List<DailyEngagement>();
        public List<SprintBurndown> Burndowns { get; set; } = new List<SprintBurndown>();
        public List<SprintVelocity> Velocities { get; set; } = new List<SprintVelocity>();

        // Filters
        public string? SelectedAssigneeId { get; set; }
        public int? SelectedProjectId { get; set; }
        
        public Microsoft.AspNetCore.Mvc.Rendering.SelectList? Assignees { get; set; }
        public Microsoft.AspNetCore.Mvc.Rendering.SelectList? Projects { get; set; }
    }

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
}
