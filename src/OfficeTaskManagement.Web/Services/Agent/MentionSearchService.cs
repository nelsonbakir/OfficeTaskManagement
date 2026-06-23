using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.Authorization;

namespace OfficeTaskManagement.Services.Agent
{
    public record MentionSearchResult(
        string Type,
        string Id,
        string Label,
        string Hint
    );

    public class MentionSearchService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permSvc;

        public MentionSearchService(ApplicationDbContext context, IPermissionService permSvc)
        {
            _context = context;
            _permSvc = permSvc;
        }

        public async Task<List<MentionSearchResult>> SearchAsync(
            string q,
            string[]? types,
            int? projectId,
            ClaimsPrincipal user,
            string tenantId,
            CancellationToken ct)
        {
            var results = new List<MentionSearchResult>();
            if (string.IsNullOrWhiteSpace(q)) return results;

            q = q.Trim().ToLower();

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var isManager = await _permSvc.HasPermissionAsync(user, Permissions.StrategicView) || 
                            await _permSvc.HasPermissionAsync(user, Permissions.WorkflowManage);
            var isLead = await _permSvc.HasPermissionAsync(user, Permissions.ProjectsManage);

            // Normalize types
            var targetTypes = types?.Select(t => t.ToLower().Trim()).ToArray() ?? new string[0];
            bool ShouldSearch(string type) => targetTypes.Length == 0 || targetTypes.Contains(type);

            // 1. Projects
            if (ShouldSearch("project"))
            {
                var projectsQuery = _context.Projects.AsQueryable();

                if (projectId.HasValue)
                {
                    projectsQuery = projectsQuery.Where(p => p.Id == projectId.Value);
                }

                if (!isManager)
                {
                    if (isLead)
                    {
                        projectsQuery = projectsQuery.Where(p => p.CreatedById == userId || 
                                                                 p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                                                 p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                    }
                    else
                    {
                        projectsQuery = projectsQuery.Where(p => p.Sprints.Any(s => s.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)) ||
                                                                 p.Epics.Any(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId))));
                    }
                }

                var matchedProjects = await projectsQuery
                    .Where(p => p.Name.ToLower().Contains(q) || (p.Description != null && p.Description.ToLower().Contains(q)))
                    .Take(5)
                    .Select(p => new MentionSearchResult(
                        "Project",
                        p.Id.ToString(),
                        p.Name,
                        "Project Workspace"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedProjects);
            }

            // 2. Epics
            if (ShouldSearch("epic"))
            {
                var epicsQuery = _context.Epics.Include(e => e.Project).AsQueryable();

                if (projectId.HasValue)
                {
                    epicsQuery = epicsQuery.Where(e => e.ProjectId == projectId.Value);
                }

                if (!isManager)
                {
                    if (isLead)
                    {
                        epicsQuery = epicsQuery.Where(e => e.Project.CreatedById == userId || 
                                                           e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                    }
                    else
                    {
                        epicsQuery = epicsQuery.Where(e => e.Features.Any(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId)));
                    }
                }

                var matchedEpics = await epicsQuery
                    .Where(e => e.Name.ToLower().Contains(q) || (e.Description != null && e.Description.ToLower().Contains(q)))
                    .Take(5)
                    .Select(e => new MentionSearchResult(
                        "Epic",
                        e.Id.ToString(),
                        e.Name,
                        e.Project != null ? $"Project: {e.Project.Name}" : "Epic Node"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedEpics);
            }

            // 3. Features
            if (ShouldSearch("feature"))
            {
                var featuresQuery = _context.Features.Include(f => f.Epic).ThenInclude(e => e.Project).AsQueryable();

                if (projectId.HasValue)
                {
                    featuresQuery = featuresQuery.Where(f => f.Epic.ProjectId == projectId.Value);
                }

                if (!isManager)
                {
                    if (isLead)
                    {
                        featuresQuery = featuresQuery.Where(f => f.Epic.Project.CreatedById == userId || 
                                                                 f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                    }
                    else
                    {
                        featuresQuery = featuresQuery.Where(f => f.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                    }
                }

                var matchedFeatures = await featuresQuery
                    .Where(f => f.Name.ToLower().Contains(q) || (f.Description != null && f.Description.ToLower().Contains(q)))
                    .Take(5)
                    .Select(f => new MentionSearchResult(
                        "Feature",
                        f.Id.ToString(),
                        f.Name,
                        f.Epic != null ? $"Epic: {f.Epic.Name}" : "Feature Node"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedFeatures);
            }

            // 4. User Stories
            if (ShouldSearch("story") || ShouldSearch("userstory"))
            {
                var storiesQuery = _context.UserStories
                    .Include(us => us.Feature)
                    .ThenInclude(f => f.Epic)
                    .ThenInclude(e => e.Project)
                    .AsQueryable();

                if (projectId.HasValue)
                {
                    storiesQuery = storiesQuery.Where(us => us.Feature.Epic.ProjectId == projectId.Value);
                }

                if (!isManager)
                {
                    if (isLead)
                    {
                        storiesQuery = storiesQuery.Where(us => us.Feature.Epic.Project.CreatedById == userId ||
                                                                us.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                    }
                    else
                    {
                        storiesQuery = storiesQuery.Where(us => us.Tasks.Any(t => t.AssigneeId == userId || t.CreatedById == userId));
                    }
                }

                var matchedStories = await storiesQuery
                    .Where(us => us.Title.ToLower().Contains(q) || (us.Description != null && us.Description.ToLower().Contains(q)))
                    .Take(5)
                    .Select(us => new MentionSearchResult(
                        "UserStory",
                        us.Id.ToString(),
                        us.Title,
                        us.Feature != null ? $"Feature: {us.Feature.Name}" : "User Story"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedStories);
            }

            // 5. Tasks
            if (ShouldSearch("task"))
            {
                var tasksQuery = _context.Tasks.Include(t => t.Project).AsQueryable();

                if (projectId.HasValue)
                {
                    tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
                }

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
                    .Where(t => t.Title.ToLower().Contains(q) || (t.Description != null && t.Description.ToLower().Contains(q)))
                    .Take(5)
                    .Select(t => new MentionSearchResult(
                        "Task",
                        t.Id.ToString(),
                        t.Title,
                        t.Project != null ? $"Project: {t.Project.Name}" : "Independent Task"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedTasks);
            }

            // 6. Sprints
            if (ShouldSearch("sprint"))
            {
                var sprintsQuery = _context.Sprints.Include(s => s.Project).AsQueryable();

                if (projectId.HasValue)
                {
                    sprintsQuery = sprintsQuery.Where(s => s.ProjectId == projectId.Value);
                }

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
                    .Take(5)
                    .Select(s => new MentionSearchResult(
                        "Sprint",
                        s.Id.ToString(),
                        s.Name,
                        s.Project != null ? $"Project: {s.Project.Name}" : "Independent Sprint"
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedSprints);
            }

            // 7. Users
            if (ShouldSearch("user"))
            {
                var usersQuery = _context.Users.Include(u => u.ResourceProfile).AsQueryable();

                if (projectId.HasValue)
                {
                    usersQuery = usersQuery.Where(u => u.ProjectAllocations.Any(pa => pa.ProjectId == projectId.Value) || 
                                                       _context.Tasks.Any(t => t.ProjectId == projectId.Value && t.AssigneeId == u.Id));
                }

                if (!isManager)
                {
                    var userProjectIds = _context.Tasks
                        .Where(t => t.AssigneeId == userId || t.CreatedById == userId)
                        .Select(t => t.ProjectId);

                    if (isLead)
                    {
                        var leadProjectIds = _context.Projects
                            .Where(p => p.CreatedById == userId)
                            .Select(p => (int?)p.Id);
                            
                        userProjectIds = userProjectIds.Union(leadProjectIds);
                    }

                    userProjectIds = userProjectIds.Distinct();

                    var userTaskIds = _context.Tasks
                        .Where(t => t.AssigneeId == userId || t.CreatedById == userId)
                        .Select(t => t.Id);

                    usersQuery = usersQuery.Where(u => 
                        _context.Tasks.Any(t => 
                            (t.AssigneeId == u.Id || t.CreatedById == u.Id) && 
                            (userProjectIds.Contains(t.ProjectId) || userTaskIds.Contains(t.Id))
                        ) || u.Id == userId
                    );
                }

                var matchedUsers = await usersQuery
                    .Where(u => (!string.IsNullOrEmpty(u.FullName) && u.FullName.ToLower().Contains(q)) || 
                                 (u.Email != null && u.Email.ToLower().Contains(q)) ||
                                 (u.UserName != null && u.UserName.ToLower().Contains(q)))
                    .Take(5)
                    .Select(u => new MentionSearchResult(
                        "User",
                        u.Id,
                        u.FullName ?? u.UserName ?? string.Empty,
                        u.ResourceProfile != null ? $"{u.ResourceProfile.Department} - {u.ResourceProfile.SeniorityLevel}" : (u.Email ?? "Team Member")
                    ))
                    .ToListAsync(ct);

                results.AddRange(matchedUsers);
            }

            return results.Take(20).ToList();
        }
    }
}
