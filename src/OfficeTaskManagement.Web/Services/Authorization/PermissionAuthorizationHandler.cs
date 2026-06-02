using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Services.Authorization
{
    /// <summary>
    /// Handles <see cref="PermissionRequirement"/> by resolving the current user's
    /// effective permissions via <see cref="IPermissionService"/>.
    /// Registered as a singleton; resolves IPermissionService from a scope to avoid
    /// captive-dependency issues.
    /// </summary>
    public class PermissionAuthorizationHandler
        : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PermissionAuthorizationHandler(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                context.Fail();
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

            if (await permissionService.HasPermissionAsync(context.User, requirement.PermissionKey))
            {
                context.Succeed(requirement);
            }
        }
    }
}
