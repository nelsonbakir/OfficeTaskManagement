using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.RoleManagement
{
    public class PermissionGroupViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSystemGroup { get; set; }
        public List<string> Keys { get; set; } = new();
        public List<string> AssignedRoleNames { get; set; } = new();
    }

    public class PermissionGroupIndexViewModel
    {
        public List<PermissionGroupViewModel> Groups { get; set; } = new();
        public IReadOnlyList<string> AllKnownKeys { get; set; } = new List<string>();
    }
}
