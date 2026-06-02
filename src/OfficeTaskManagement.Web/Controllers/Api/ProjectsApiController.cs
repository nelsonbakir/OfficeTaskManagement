using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.Authorization;
using OfficeTaskManagement.Data;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class ProjectsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProjectsApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProjects([FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var isManager = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) || await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage);
            var isLead = await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);

            var query = _context.Projects.AsQueryable();

            if (!isManager)
            {
                if (isLead)
                {
                    query = query.Where(p => p.CreatedById == userId ||
                                             p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else
                {
                    query = query.Where(p => p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                             p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
            }

            var projects = await query.Select(p => new
            {
                p.Id,
                p.Name,
                p.Description
            }).ToListAsync();

            return Ok(projects);
        }
    }
}
