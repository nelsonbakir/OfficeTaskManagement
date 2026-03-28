using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    public class ResourceProfileViewModel
    {
        public string UserId { get; set; } = string.Empty;
        
        [Display(Name = "Name")]
        public string FullName { get; set; } = string.Empty;
        
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Department")]
        public string? Department { get; set; }

        [Display(Name = "Seniority")]
        public SeniorityLevel SeniorityLevel { get; set; }

        [Display(Name = "Daily Capacity (Hours)")]
        [Range(0.5, 24)]
        public decimal DailyCapacityHours { get; set; }

        [Display(Name = "Hourly Cost Rate")]
        public decimal HourlyRate { get; set; }

        public string? Notes { get; set; }

        public List<ResourceSkillViewModel> Skills { get; set; } = new();
        public List<ProjectAllocationSummaryViewModel> ActiveAllocations { get; set; } = new();
        public List<AvailabilityBlockViewModel> AvailabilityBlocks { get; set; } = new();

        // Used to show the manager status on the UI
        public decimal UtilizationPercent { get; set; }
        public bool IsOverAllocated { get; set; }

        [Display(Name = "Is Schedulable Resource")]
        public bool IsResource { get; set; } = true;
    }

    public class ResourceSkillViewModel
    {
        public int Id { get; set; }
        public string SkillName { get; set; } = string.Empty;
        public ProficiencyLevel ProficiencyLevel { get; set; }
    }

    public class ProjectAllocationSummaryViewModel
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int AllocationPercentage { get; set; }
        public string? ProjectRole { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class AvailabilityBlockViewModel
    {
        public int Id { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public AvailabilityBlockReason Reason { get; set; }
        public LeaveApprovalStatus ApprovalStatus { get; set; }
        public string? Notes { get; set; }
    }
}
