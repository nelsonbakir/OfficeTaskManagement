using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Data
{
    /// <summary>
    /// Seeds the initial reference data: roles, permission groups, and the super-admin user.
    /// Safe to run on every startup — all operations are idempotent.
    /// </summary>
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<AppRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var context     = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // ── 1. Permission Groups ─────────────────────────────────────────
            await SeedPermissionGroupsAsync(context);

            // ── 2. Roles ─────────────────────────────────────────────────────
            await SeedRolesAsync(roleManager, context);

            // ── 3. Default Super Admin user ──────────────────────────────────
            await SeedSuperAdminAsync(userManager);

            // ── 4. Reference data ────────────────────────────────────────────
            await SeedAreasAsync(context);
            await SeedPublicHolidaysAsync(context);
        }

        // ── Permission Groups ─────────────────────────────────────────────────

        private static async Task SeedPermissionGroupsAsync(ApplicationDbContext context)
        {
            var groups = new[]
            {
                new
                {
                    Name        = "System Administration",
                    Description = "Full control over users, roles, holidays, and system settings.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.UsersView, Permissions.UsersManage, Permissions.RolesManage, Permissions.HolidaysManage }
                },
                new
                {
                    Name        = "Project Management",
                    Description = "Create and manage projects, epics, features, and portfolio decisions.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.ProjectsView, Permissions.ProjectsManage, Permissions.EpicsManage, Permissions.FeaturesManage, Permissions.StrategicView, Permissions.StrategicManage }
                },
                new
                {
                    Name        = "Resource Management",
                    Description = "Manage resource profiles, allocations, and capacity planning.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.ResourcesView, Permissions.ResourcesManage, Permissions.CapacityView }
                },
                new
                {
                    Name        = "Salary Management",
                    Description = "View and manage salary records. Highly restricted.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.SalaryView, Permissions.SalaryManage }
                },
                new
                {
                    Name        = "Work Management",
                    Description = "Create, edit, and manage tasks, sprints, and backlogs.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.TasksManage, Permissions.SprintsManage }
                },
                new
                {
                    Name        = "Planning",
                    Description = "Manage user stories and workflow templates.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.EpicsManage, Permissions.FeaturesManage, Permissions.WorkflowManage }
                },
                new
                {
                    Name        = "Quality Assurance",
                    Description = "Create and manage test cases.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.TestCasesManage }
                },
                new
                {
                    Name        = "Analytics & Insights",
                    Description = "Access analytics dashboard and AI-powered insights.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.AnalyticsView, Permissions.AnalyticsAI }
                },
                new
                {
                    Name        = "Read Only",
                    Description = "View-only access. No create, edit, or delete operations.",
                    IsSystem    = true,
                    Keys        = new[] { Permissions.ProjectsView, Permissions.AnalyticsView, Permissions.ResourcesView }
                },
            };

            foreach (var g in groups)
            {
                var existing = await context.PermissionGroups
                    .Include(p => p.Permissions)
                    .FirstOrDefaultAsync(p => p.Name == g.Name);

                if (existing == null)
                {
                    var group = new PermissionGroup
                    {
                        Name        = g.Name,
                        Description = g.Description,
                        IsSystemGroup = g.IsSystem,
                        Permissions = g.Keys.Select(k => new PermissionGroupKey { Key = k }).ToList()
                    };
                    context.PermissionGroups.Add(group);
                }
            }

            await context.SaveChangesAsync();
        }

        // ── Roles ─────────────────────────────────────────────────────────────

        private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager, ApplicationDbContext context)
        {
            // Helper: get PermissionGroup by name (already persisted above)
            async Task<PermissionGroup?> Group(string name) =>
                await context.PermissionGroups.FirstOrDefaultAsync(g => g.Name == name);

            var roleDefs = new[]
            {
                new
                {
                    Name   = "Super Admin",
                    Desc   = "Unrestricted system-wide access. Typically CEO / Executive Director.",
                    Color  = "#dc3545",
                    Icon   = "fas fa-crown",
                    Level  = 0,
                    Groups = new string[] { } // Super Admin bypasses group checks entirely
                },
                new
                {
                    Name   = "Admin",
                    Desc   = "System administrator. Full access including user, role, and salary management.",
                    Color  = "#6610f2",
                    Icon   = "fas fa-shield-alt",
                    Level  = 1,
                    Groups = new[] { "System Administration", "Project Management", "Resource Management", "Salary Management", "Work Management", "Planning", "Quality Assurance", "Analytics & Insights" }
                },
                new
                {
                    Name   = "Project Manager",
                    Desc   = "Oversees project portfolio, resources, capacity, and strategic decisions.",
                    Color  = "#0d6efd",
                    Icon   = "fas fa-user-tie",
                    Level  = 2,
                    Groups = new[] { "Project Management", "Resource Management", "Salary Management", "Work Management", "Planning", "Quality Assurance", "Analytics & Insights" }
                },
                new
                {
                    Name   = "Project Lead",
                    Desc   = "Leads a project team. Manages epics, features, and sprint planning.",
                    Color  = "#0dcaf0",
                    Icon   = "fas fa-user-astronaut",
                    Level  = 3,
                    Groups = new[] { "Project Management", "Resource Management", "Work Management", "Planning", "Quality Assurance", "Analytics & Insights" }
                },
                new
                {
                    Name   = "Project Coordinator",
                    Desc   = "Coordinates schedules, sprints, and workflow templates.",
                    Color  = "#20c997",
                    Icon   = "fas fa-people-arrows",
                    Level  = 4,
                    Groups = new[] { "Work Management", "Planning", "Quality Assurance", "Analytics & Insights", "Read Only" }
                },
                new
                {
                    Name   = "Developer",
                    Desc   = "Executes tasks and manages their own work items within assigned sprints.",
                    Color  = "#198754",
                    Icon   = "fas fa-code",
                    Level  = 5,
                    Groups = new[] { "Work Management", "Quality Assurance" }
                },
                new
                {
                    Name   = "QA Engineer",
                    Desc   = "Focuses on test cases, task validation, and quality gates.",
                    Color  = "#fd7e14",
                    Icon   = "fas fa-vial",
                    Level  = 6,
                    Groups = new[] { "Quality Assurance", "Work Management" }
                },
                new
                {
                    Name   = "Client",
                    Desc   = "External stakeholder with read-only access to project status.",
                    Color  = "#6c757d",
                    Icon   = "fas fa-handshake",
                    Level  = 7,
                    Groups = new[] { "Read Only" }
                },
            };

            foreach (var rd in roleDefs)
            {
                var existing = await roleManager.FindByNameAsync(rd.Name);

                if (existing == null)
                {
                    existing = new AppRole
                    {
                        Name           = rd.Name,
                        Description    = rd.Desc,
                        Color          = rd.Color,
                        Icon           = rd.Icon,
                        HierarchyLevel = rd.Level,
                        IsSystemRole   = true
                    };
                    await roleManager.CreateAsync(existing);
                }
                else
                {
                    // Update metadata on existing roles
                    existing.Description    = rd.Desc;
                    existing.Color          = rd.Color;
                    existing.Icon           = rd.Icon;
                    existing.HierarchyLevel = rd.Level;
                    existing.IsSystemRole   = true;
                    await roleManager.UpdateAsync(existing);
                }

                // Sync permission groups
                var roleEntity = await context.Roles
                    .Include(r => r.PermissionGroups)
                    .FirstOrDefaultAsync(r => r.Id == existing.Id);

                if (roleEntity == null) continue;

                // Remove any groups not in the definition
                var toRemove = roleEntity.PermissionGroups
                    .Where(rpg => !rd.Groups.Contains(rpg.PermissionGroup?.Name ?? ""))
                    .ToList();

                // Load groups for removal — need full nav
                var existingGroups = await context.AppRolePermissionGroups
                    .Include(x => x.PermissionGroup)
                    .Where(x => x.RoleId == roleEntity.Id)
                    .ToListAsync();

                var toRemoveIds = existingGroups
                    .Where(x => !rd.Groups.Contains(x.PermissionGroup.Name))
                    .ToList();

                context.AppRolePermissionGroups.RemoveRange(toRemoveIds);

                // Add missing groups
                var currentGroupNames = existingGroups
                    .Where(x => !toRemoveIds.Contains(x))
                    .Select(x => x.PermissionGroup.Name)
                    .ToHashSet();

                foreach (var groupName in rd.Groups)
                {
                    if (currentGroupNames.Contains(groupName)) continue;
                    var pg = await Group(groupName);
                    if (pg == null) continue;

                    context.AppRolePermissionGroups.Add(new AppRolePermissionGroup
                    {
                        RoleId           = roleEntity.Id,
                        PermissionGroupId = pg.Id
                    });
                }
            }

            await context.SaveChangesAsync();

            // ── Legacy role cleanup: remove old "Manager", "Employee", "Project Coordinator" 
            //    (the seed used to use these names; now replaced by the above set)
            var legacyRoles = new[] { "Manager", "Employee" };
            foreach (var legacyName in legacyRoles)
            {
                var legacy = await roleManager.FindByNameAsync(legacyName);
                if (legacy != null && !legacy.IsSystemRole)
                    await roleManager.DeleteAsync(legacy);
            }
        }

        // ── Super Admin User ──────────────────────────────────────────────────

        private static async Task SeedSuperAdminAsync(UserManager<User> userManager)
        {
            const string superAdminEmail = "superadmin@taskflow.com";
            var superAdmin = await userManager.FindByEmailAsync(superAdminEmail);

            if (superAdmin == null)
            {
                superAdmin = new User
                {
                    UserName       = superAdminEmail,
                    Email          = superAdminEmail,
                    FullName       = "Super Administrator",
                    JobTitle       = "Executive Director",
                    Department     = "Management",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(superAdmin, "SuperAdmin@TaskFlow!2026");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(superAdmin, "Super Admin");
                }
            }

            // Also keep the legacy admin@example.com as Admin role for backward compat
            const string legacyAdminEmail = "admin@example.com";
            var legacyAdmin = await userManager.FindByEmailAsync(legacyAdminEmail);
            if (legacyAdmin == null)
            {
                legacyAdmin = new User
                {
                    UserName       = legacyAdminEmail,
                    Email          = legacyAdminEmail,
                    FullName       = "Admin User",
                    JobTitle       = "System Administrator",
                    Department     = "IT",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(legacyAdmin, "YourDefaultAdminPassword@123!");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(legacyAdmin, "Admin");
            }
            else
            {
                // Migrate legacy admin from "Manager" → "Admin" if needed
                var roles = await userManager.GetRolesAsync(legacyAdmin);
                if (roles.Contains("Manager") && !roles.Contains("Admin"))
                {
                    await userManager.RemoveFromRoleAsync(legacyAdmin, "Manager");
                    await userManager.AddToRoleAsync(legacyAdmin, "Admin");
                }
            }
        }

        // ── Reference Data ────────────────────────────────────────────────────

        private static async Task SeedAreasAsync(ApplicationDbContext context)
        {
            if (!context.Areas.Any())
            {
                context.Areas.AddRange(new List<Area>
                {
                    new Area { Name = "Web API" },
                    new Area { Name = "Frontend" },
                    new Area { Name = "Database" },
                    new Area { Name = "Mobile" },
                    new Area { Name = "Full-stack" }
                });
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedPublicHolidaysAsync(ApplicationDbContext context)
        {
            if (!context.PublicHolidays.Any())
            {
                context.PublicHolidays.AddRange(new List<PublicHoliday>
                {
                    new PublicHoliday { Name = "Independence Day", FromDate = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = true },
                    new PublicHoliday { Name = "Victory Day",      FromDate = new DateTime(2026, 12, 16, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 12, 16, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = true },
                    new PublicHoliday { Name = "May Day",          FromDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),  ToDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),  IsFixedDate = true },
                    new PublicHoliday { Name = "Eid-ul-Fitr",      FromDate = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = false }
                });
                await context.SaveChangesAsync();
            }
        }
    }
}
