using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.ViewModels.UserManagement
{
    public class EditUserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string? Email { get; set; }

        [Required]
        public string? FullName { get; set; }

        public string? Department { get; set; }
        public string? JobTitle { get; set; }

        public List<string> SelectedRoles { get; set; } = new();
        public List<string> AvailableRoles { get; set; } = new();

        public bool IsActive { get; set; }
        public IFormFile? Avatar { get; set; }
        public string? AvatarPath { get; set; }
    }
}
