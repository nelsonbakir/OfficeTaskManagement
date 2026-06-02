using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Services.Authorization
{
    /// <summary>
    /// Resolves a user's effective permissions by walking Role → PermissionGroup → PermissionGroupKey.
    /// Results are cached per user per request via IMemoryCache with a short sliding window.
    /// Super Admin (HierarchyLevel == 0) is implicitly granted all known permission keys.
    /// </summary>
    public class PermissionService : IPermissionService
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<AppRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public PermissionService(
            UserManager<User> userManager,
            RoleManager<AppRole> roleManager,
            ApplicationDbContext context,
            IMemoryCache cache)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _cache = cache;
        }

        /// <inheritdoc/>
        public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionKey)
        {
            var permissions = await GetUserPermissionsAsync(principal);
            return permissions.Contains(permissionKey);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> GetUserPermissionsAsync(ClaimsPrincipal principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return Enumerable.Empty<string>();

            var userId = _userManager.GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Enumerable.Empty<string>();

            var cacheKey = $"user_permissions_{userId}";
            if (_cache.TryGetValue(cacheKey, out IEnumerable<string>? cached) && cached != null)
                return cached;

            var permissions = await ResolvePermissionsAsync(userId);

            _cache.Set(cacheKey, permissions, new MemoryCacheEntryOptions
            {
                SlidingExpiration = CacheDuration
            });

            return permissions;
        }

        // ── Internal ─────────────────────────────────────────────────────────

        /// <summary>Invalidates cached permissions for a given user (call after role/group changes).</summary>
        public void InvalidateCache(string userId)
        {
            _cache.Remove($"user_permissions_{userId}");
        }

        private async Task<HashSet<string>> ResolvePermissionsAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return new HashSet<string>();

            var roleNames = await _userManager.GetRolesAsync(user);

            // Load AppRole entities to check hierarchy level
            var appRoles = await _roleManager.Roles
                .Where(r => roleNames.Contains(r.Name!))
                .Include(r => r.PermissionGroups)
                    .ThenInclude(rpg => rpg.PermissionGroup)
                        .ThenInclude(pg => pg.Permissions)
                .ToListAsync();

            // Super Admin (HierarchyLevel == 0) gets all permissions implicitly
            if (appRoles.Any(r => r.HierarchyLevel == 0))
            {
                return new HashSet<string>(Permissions.All);
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var role in appRoles)
            {
                foreach (var rpg in role.PermissionGroups)
                {
                    foreach (var key in rpg.PermissionGroup.Permissions)
                    {
                        keys.Add(key.Key);
                    }
                }
            }

            return keys;
        }
    }
}
