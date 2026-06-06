using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Extended Identity Role that carries hierarchy metadata, branding, and
    /// links to one or more Permission Groups.
    /// </summary>
    public class AppRole : IdentityRole, IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;
        /// <summary>Short explanation shown in the admin UI.</summary>
        public string? Description { get; set; }

        /// <summary>CSS / hex colour used for the badge chip in the UI (e.g. "#6610f2").</summary>
        public string? Color { get; set; }

        /// <summary>FontAwesome class for the sidebar / badge icon (e.g. "fas fa-crown").</summary>
        public string? Icon { get; set; }

        /// <summary>
        /// Organisational authority level: 0 = highest (Super Admin), larger = lower authority.
        /// Used for display ordering and preventing privilege escalation.
        /// </summary>
        public int HierarchyLevel { get; set; }

        /// <summary>
        /// System roles (seeded defaults) cannot be deleted via the UI to prevent
        /// locking the system. They can still be edited (colour, description, groups).
        /// </summary>
        public bool IsSystemRole { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public ICollection<AppRolePermissionGroup> PermissionGroups { get; set; }
            = new List<AppRolePermissionGroup>();
    }
}
