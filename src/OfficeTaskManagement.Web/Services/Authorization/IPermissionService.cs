using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OfficeTaskManagement.Services.Authorization
{
    /// <summary>
    /// Provides methods for checking a user's effective permissions, which are
    /// the union of all permission keys across all of their roles' permission groups.
    /// </summary>
    public interface IPermissionService
    {
        /// <summary>
        /// Returns true if the specified user holds the given permission key
        /// (directly or via any of their role's permission groups).
        /// Super Admin is implicitly granted all permissions.
        /// </summary>
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionKey);

        /// <summary>Returns the full set of effective permission keys for the user.</summary>
        Task<IEnumerable<string>> GetUserPermissionsAsync(ClaimsPrincipal user);
    }
}
