using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.ViewModels;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class SearchController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SearchController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return View(new SearchResultViewModel { Query = q ?? string.Empty });
            }

            q = q.Trim().ToLower();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isManager = User.IsInRole("Manager") || User.IsInRole("Project Coordinator");
            var isLead = User.IsInRole("Project Lead");

            var results = new SearchResultViewModel { Query = q };

            // ============================================
            // 1. Search Tasks (Title, Description, Comments)
            // ============================================
            var tasksQuery = _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Comments)
                .AsQueryable();

            if (!isManager)
            {
                if (isLead)
                {
                    tasksQuery = tasksQuery.Where(t => t.CreatedById == userId ||
                                                       t.AssigneeId == userId ||
                                                       (t.Project != null && t.Project.CreatedById == userId));
                }
                else
                {
                    tasksQuery = tasksQuery.Where(t => t.AssigneeId == userId || t.CreatedById == userId);
                }
            }

            var matchedTasks = await tasksQuery
                .Where(t => t.Title.ToLower().Contains(q) || 
                            (t.Description != null && t.Description.ToLower().Contains(q)) || 
                            t.Comments.Any(c => c.CommentText.ToLower().Contains(q)))
                .Take(20)
                .ToListAsync();

            results.Tasks = matchedTasks.Select(t => new SearchHit
            {
                Title = t.Title,
                Description = t.Description ?? string.Empty,
                Url = Url.Action("Details", "TaskItems", new { id = t.Id }) ?? string.Empty,
                ContextHint = t.Project != null ? $"Project: {t.Project.Name}" : "Independent Task"
            }).ToList();

            // ============================================
            // 2. Search Projects
            // ============================================
            var projectsQuery = _context.Projects.AsQueryable();

            if (!isManager)
            {
                if (isLead)
                {
                    projectsQuery = projectsQuery.Where(p => p.CreatedById == userId || 
                                                             p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                                             p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
                else // Employee
                {
                    projectsQuery = projectsQuery.Where(p => p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                                             p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                }
            }

            var matchedProjects = await projectsQuery
                .Where(p => p.Name.ToLower().Contains(q) || (p.Description != null && p.Description.ToLower().Contains(q)))
                .Take(10)
                .ToListAsync();

            results.Projects = matchedProjects.Select(p => new SearchHit
            {
                Title = p.Name,
                Description = p.Description ?? string.Empty,
                Url = Url.Action("Details", "Projects", new { id = p.Id }) ?? string.Empty,
                ContextHint = "Project Workspace"
            }).ToList();

            // ============================================
            // 3. Search Sprints
            // ============================================
            var sprintsQuery = _context.Sprints.Include(s => s.Project).AsQueryable();

            if (!isManager)
            {
                if (isLead)
                {
                    sprintsQuery = sprintsQuery.Where(s => s.Project.CreatedById == userId || 
                                                           s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
                else
                {
                    sprintsQuery = sprintsQuery.Where(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
            }

            var matchedSprints = await sprintsQuery
                .Where(s => s.Name.ToLower().Contains(q))
                .Take(10)
                .ToListAsync();

            results.Sprints = matchedSprints.Select(s => new SearchHit
            {
                Title = s.Name,
                Description = $"{(s.IsActive ? "Active" : "Completed")} Sprint",
                Url = Url.Action("Details", "Sprints", new { id = s.Id }) ?? string.Empty,
                ContextHint = s.Project != null ? $"Project: {s.Project.Name}" : "Independent Sprint"
            }).ToList();

            // ============================================
            // 4. Search Epics & Features
            // ============================================
            var epicsQuery = _context.Epics.Include(e => e.Project).AsQueryable();
            var featuresQuery = _context.Features.Include(f => f.Epic).AsQueryable();

            if (!isManager)
            {
                if (isLead)
                {
                    epicsQuery = epicsQuery.Where(e => e.Project.CreatedById == userId || 
                                                       e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                    featuresQuery = featuresQuery.Where(f => f.Epic.Project.CreatedById == userId || 
                                                             f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
                else
                {
                    epicsQuery = epicsQuery.Where(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                    featuresQuery = featuresQuery.Where(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                }
            }

            var matchedEpics = await epicsQuery
                .Where(e => e.Name.ToLower().Contains(q) || (e.Description != null && e.Description.ToLower().Contains(q)))
                .Take(10)
                .ToListAsync();

            results.Epics = matchedEpics.Select(e => new SearchHit
            {
                Title = e.Name,
                Description = e.Description ?? string.Empty,
                Url = Url.Action("Details", "Epics", new { id = e.Id }) ?? string.Empty,
                ContextHint = e.Project != null ? $"Project: {e.Project.Name}" : "Epic Node"
            }).ToList();

            var matchedFeatures = await featuresQuery
                .Where(f => f.Name.ToLower().Contains(q) || (f.Description != null && f.Description.ToLower().Contains(q)))
                .Take(10)
                .ToListAsync();

            results.Features = matchedFeatures.Select(f => new SearchHit
            {
                Title = f.Name,
                Description = f.Description ?? string.Empty,
                Url = Url.Action("Details", "Features", new { id = f.Id }) ?? string.Empty,
                ContextHint = f.Epic != null ? $"Epic: {f.Epic.Name}" : "Feature Node"
            }).ToList();

            // ============================================
            // 5. Search Users (Teammates)
            // ============================================
            var matchedUsers = await _context.Users
                .Where(u => (!string.IsNullOrEmpty(u.FullName) && u.FullName.ToLower().Contains(q)) || 
                             (u.Email != null && u.Email.ToLower().Contains(q)))
                .Take(10)
                .ToListAsync();

            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (isAjax)
            {
                return PartialView("_SearchResultsOverlay", results);
            }

            return View(results);
        }
    }
}
