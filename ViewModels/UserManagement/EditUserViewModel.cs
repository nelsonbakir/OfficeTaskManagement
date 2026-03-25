using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.UserManagement
{
    public class EditUserViewModel
    {
        public string Id { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public List<string> SelectedRoles { get; set; } = new List<string>();
        public List<string> AvailableRoles { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        public IFormFile? Avatar { get; set; }
        public string? AvatarPath { get; set; }
    }
}
