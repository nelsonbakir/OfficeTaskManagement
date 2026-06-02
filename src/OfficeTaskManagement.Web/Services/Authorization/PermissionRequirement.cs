using Microsoft.AspNetCore.Authorization;

namespace OfficeTaskManagement.Services.Authorization
{
    /// <summary>
    /// Carries the permission key (e.g. "projects.manage") that must be held by the user.
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionKey { get; }

        public PermissionRequirement(string permissionKey)
        {
            PermissionKey = permissionKey;
        }
    }
}
