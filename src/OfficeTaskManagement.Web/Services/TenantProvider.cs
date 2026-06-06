using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System;
using System.Linq;
using System.Security.Claims;

namespace OfficeTaskManagement.Services
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private string? _tenantId;

        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string TenantId
        {
            get
            {
                if (_tenantId != null)
                {
                    return _tenantId;
                }

                _tenantId = ResolveTenantId();
                return _tenantId;
            }
        }

        public void SetTenant(string tenantId)
        {
            _tenantId = tenantId;
        }

        private string ResolveTenantId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                // Fallback for background tasks or migrations
                return string.Empty;
            }

            // 1. Resolve from User Claims (for authenticated users)
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                var claimTenantId = httpContext.User.FindFirstValue("TenantId");
                if (!string.IsNullOrEmpty(claimTenantId))
                {
                    return claimTenantId;
                }
            }

            // 2. Resolve from Query String
            if (httpContext.Request.Query.TryGetValue("tenantId", out var queryTenantId))
            {
                var val = queryTenantId.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            if (httpContext.Request.Query.TryGetValue("tenant", out var queryTenant))
            {
                var val = queryTenant.ToString();
                if (!string.IsNullOrEmpty(val))
                {
                    var resolvedId = ResolveTenantIdFromSlugOrId(val);
                    if (!string.IsNullOrEmpty(resolvedId)) return resolvedId;
                }
            }

            // 3. Resolve from Headers
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenantId))
            {
                var val = headerTenantId.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            if (httpContext.Request.Headers.TryGetValue("X-Tenant", out var headerTenant))
            {
                var val = headerTenant.ToString();
                if (!string.IsNullOrEmpty(val))
                {
                    var resolvedId = ResolveTenantIdFromSlugOrId(val);
                    if (!string.IsNullOrEmpty(resolvedId)) return resolvedId;
                }
            }

            // 4. Resolve from Cookies
            if (httpContext.Request.Cookies.TryGetValue("TenantId", out var cookieTenantId))
            {
                if (!string.IsNullOrEmpty(cookieTenantId)) return cookieTenantId;
            }

            // 5. Default fallback to the first tenant in the database to prevent crashes
            // in MVC UI pages when no tenant is explicitly requested.
            var defaultTenantId = ResolveFirstTenantId();
            if (!string.IsNullOrEmpty(defaultTenantId))
            {
                return defaultTenantId;
            }

            return string.Empty;
        }

        private string ResolveTenantIdFromSlugOrId(string value)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return string.Empty;

            try
            {
                using (var scope = httpContext.RequestServices.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    // Disable tenant query filter mapping during resolution to prevent recursion
                    var tenant = dbContext.Set<Tenant>().FirstOrDefault(t => t.Id == value || t.Identifier == value);
                    return tenant?.Id ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveFirstTenantId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return string.Empty;

            try
            {
                using (var scope = httpContext.RequestServices.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var tenant = dbContext.Set<Tenant>().FirstOrDefault();
                    return tenant?.Id ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
