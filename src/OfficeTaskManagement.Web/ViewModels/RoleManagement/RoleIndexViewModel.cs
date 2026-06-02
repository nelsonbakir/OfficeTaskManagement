using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.RoleManagement
{
    public class RoleListViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Color { get; set; }
        public string? Icon { get; set; }
        public int HierarchyLevel { get; set; }
        public bool IsSystemRole { get; set; }
        public int UserCount { get; set; }
        public List<string> PermissionGroupNames { get; set; } = new();
    }

    public class RoleIndexViewModel
    {
        public List<RoleListViewModel> Roles { get; set; } = new();
    }
}
