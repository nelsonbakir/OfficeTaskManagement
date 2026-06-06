namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Many-to-many join between <see cref="AppRole"/> and <see cref="PermissionGroup"/>.
    /// A role may be assigned any number of permission groups; the effective permissions
    /// for a user are the union of all keys across all groups assigned to all of the
    /// user's roles.
    /// </summary>
    public class AppRolePermissionGroup : IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
        public int PermissionGroupId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public AppRole Role { get; set; } = null!;
        public PermissionGroup PermissionGroup { get; set; } = null!;
    }
}
