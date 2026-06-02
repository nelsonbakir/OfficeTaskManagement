using System.Collections.Generic;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// A named collection of granular permission keys.
    /// One or many Permission Groups are assigned to an AppRole, giving that role
    /// the cumulative set of all keys across its groups.
    /// </summary>
    public class PermissionGroup
    {
        public int Id { get; set; }

        /// <summary>Human-readable name, e.g. "Project Management".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional description shown in the admin UI.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// System groups (seeded defaults) cannot be deleted via the UI.
        /// They can still be edited for non-key aspects.
        /// </summary>
        public bool IsSystemGroup { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public ICollection<PermissionGroupKey> Permissions { get; set; }
            = new List<PermissionGroupKey>();

        public ICollection<AppRolePermissionGroup> Roles { get; set; }
            = new List<AppRolePermissionGroup>();
    }
}
