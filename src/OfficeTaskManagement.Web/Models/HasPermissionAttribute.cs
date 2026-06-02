using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Services.Authorization;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Replaces [Authorize(Roles = "...")] with a fine-grained permission check.
    /// Usage:  [HasPermission(Permissions.ProjectsManage)]
    /// </summary>
    public class HasPermissionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _permissionKey;

        public HasPermissionAttribute(string permissionKey)
        {
            _permissionKey = permissionKey;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var permissionService = context.HttpContext.RequestServices
                .GetRequiredService<IPermissionService>();

            if (!await permissionService.HasPermissionAsync(user, _permissionKey))
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
