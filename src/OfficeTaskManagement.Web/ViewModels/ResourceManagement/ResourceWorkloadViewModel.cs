using System;
using System.Collections.Generic;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    public class ResourceWorkloadViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? AvatarPath { get; set; }
        
        public List<ProjectAllocationSummaryViewModel> ActiveAllocations { get; set; } = new();
        public List<TaskItem> AssignedTasks { get; set; } = new();
    }
}
