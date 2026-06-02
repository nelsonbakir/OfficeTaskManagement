using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.ViewModels.RoleManagement
{
    public class CreateRoleViewModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>Hex colour code for the UI badge.</summary>
        public string Color { get; set; } = "#6c757d";

        /// <summary>FontAwesome class, e.g. "fas fa-user".</summary>
        public string Icon { get; set; } = "fas fa-user";

        /// <summary>0 = highest authority. Used for display ordering.</summary>
        [Range(1, 99, ErrorMessage = "Hierarchy level must be between 1 and 99 (0 is reserved for Super Admin).")]
        public int HierarchyLevel { get; set; } = 10;

        /// <summary>IDs of permission groups assigned to this role.</summary>
        public List<int> SelectedGroupIds { get; set; } = new();

        // Populated by controller for the picker UI
        public List<PermissionGroupPickerItem> AvailableGroups { get; set; } = new();
    }

    public class PermissionGroupPickerItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> Keys { get; set; } = new();
    }
}
