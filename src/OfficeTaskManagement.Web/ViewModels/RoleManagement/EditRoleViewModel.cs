using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.ViewModels.RoleManagement
{
    public class EditRoleViewModel
    {
        public string Id { get; set; } = string.Empty;
        public bool IsSystemRole { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public string Color { get; set; } = "#6c757d";
        public string Icon { get; set; } = "fas fa-user";

        [Range(0, 99)]
        public int HierarchyLevel { get; set; }

        public List<int> SelectedGroupIds { get; set; } = new();
        public List<PermissionGroupPickerItem> AvailableGroups { get; set; } = new();
    }
}
