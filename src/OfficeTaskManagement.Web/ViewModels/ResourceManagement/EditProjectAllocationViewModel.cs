using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    public class EditProjectAllocationViewModel
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }
        
        public string? ProjectName { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        
        public string? UserName { get; set; }

        [Required]
        [Range(1, 100)]
        [Display(Name = "Allocation Percentage")]
        public int AllocationPercentage { get; set; } = 100;

        [Display(Name = "Role in Project")]
        public string? ProjectRole { get; set; }

        [Required]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; } = DateTime.Today;

        [Display(Name = "End Date")]
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Optional URL to redirect to after a successful save.
        /// Set by callers (e.g. the AI Sprint Planner wizard) that want
        /// the user returned to a specific page rather than Projects/Details.
        /// Must pass <see cref="Microsoft.AspNetCore.Mvc.IUrlHelper.IsLocalUrl"/> check.
        /// </summary>
        public string? ReturnUrl { get; set; }
    }
}
