using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.WorkflowEngine;
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
            var tenantProvider = serviceProvider.GetRequiredService<ITenantProvider>();

            // ── 0. Default Tenants ───────────────────────────────────────────
            var defaultTenant = await context.Set<Tenant>().FirstOrDefaultAsync(t => t.Identifier == "taskflow");
            if (defaultTenant == null)
            {
                defaultTenant = new Tenant
                {
                    Id = "default-tenant-id",
                    Name = "TaskFlow Corp",
                    Identifier = "taskflow"
                };
                context.Set<Tenant>().Add(defaultTenant);

                var acmeTenant = new Tenant
                {
                    Id = "acme-tenant-id",
                    Name = "Acme Inc",
                    Identifier = "acme"
                };
                context.Set<Tenant>().Add(acmeTenant);

                await context.SaveChangesAsync();
            }

            // Set the active tenant context for the rest of the seeding process
            tenantProvider.SetTenant(defaultTenant.Id);

            // ── 1. Permission Groups ─────────────────────────────────────────
            await SeedPermissionGroupsAsync(context);

            // ── 2. Roles ─────────────────────────────────────────────────────
            await SeedRolesAsync(roleManager, context);

            // ── 3. Default Super Admin user ──────────────────────────────────
            await SeedSuperAdminAsync(userManager);

            // ── 4. Reference data ────────────────────────────────────────────
            await SeedAreasAsync(context);
            await SeedPublicHolidaysAsync(context);

            // ── 5. Rich Sample Data ──────────────────────────────────────────
            await SeedSampleDataAsync(context, userManager, serviceProvider);
        }

        // ── Permission Groups ─────────────────────────────────────────────────

        private static async Task SeedPermissionGroupsAsync(ApplicationDbContext context)
        {
            var tenantId = context.CurrentTenantId;
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
                    Keys        = new[] { Permissions.ProjectsView, Permissions.ProjectsManage, Permissions.EpicsManage, Permissions.FeaturesManage, Permissions.StrategicView, Permissions.StrategicManage, Permissions.BudgetView, Permissions.BudgetManage }
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
                    Keys        = new[] { Permissions.ProjectsView, Permissions.AnalyticsView, Permissions.ResourcesView, Permissions.BudgetView }
                },
            };

            foreach (var g in groups)
            {
                var existing = await context.PermissionGroups.IgnoreQueryFilters()
                    .Include(p => p.Permissions)
                    .FirstOrDefaultAsync(p => p.Name == g.Name && p.TenantId == tenantId);

                if (existing == null)
                {
                    var group = new PermissionGroup
                    {
                        Name        = g.Name,
                        Description = g.Description,
                        IsSystemGroup = g.IsSystem,
                        Permissions = g.Keys.Select(k => new PermissionGroupKey { Key = k, TenantId = tenantId }).ToList(),
                        TenantId = tenantId
                    };
                    context.PermissionGroups.Add(group);
                }
                else
                {
                    if (existing.TenantId != tenantId)
                    {
                        existing.TenantId = tenantId;
                        foreach (var perm in existing.Permissions)
                        {
                            perm.TenantId = tenantId;
                        }
                    }

                    if (existing.IsSystemGroup)
                    {
                        var existingKeys = existing.Permissions.Select(p => p.Key).ToHashSet();
                        var targetKeys = g.Keys.ToHashSet();

                        // Add missing permissions
                        foreach (var targetKey in targetKeys)
                        {
                            if (!existingKeys.Contains(targetKey))
                            {
                                existing.Permissions.Add(new PermissionGroupKey
                                {
                                    Key = targetKey,
                                    TenantId = tenantId
                                });
                            }
                        }

                        // Remove obsolete permissions
                        var toRemove = existing.Permissions.Where(p => !targetKeys.Contains(p.Key)).ToList();
                        foreach (var permToRemove in toRemove)
                        {
                            existing.Permissions.Remove(permToRemove);
                            context.PermissionGroupKeys.Remove(permToRemove);
                        }
                    }
                }
            }

            await context.SaveChangesAsync();
        }

        // ── Roles ─────────────────────────────────────────────────────────────

        private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager, ApplicationDbContext context)
        {
            var tenantId = context.CurrentTenantId;
            // Helper: get PermissionGroup by name (already persisted above)
            async Task<PermissionGroup?> Group(string name) =>
                await context.PermissionGroups.IgnoreQueryFilters().FirstOrDefaultAsync(g => g.Name == name && g.TenantId == tenantId);

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
                var existing = await roleManager.Roles.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Name == rd.Name && r.TenantId == tenantId);

                if (existing == null)
                {
                    existing = new AppRole
                    {
                        Name           = rd.Name,
                        Description    = rd.Desc,
                        Color          = rd.Color,
                        Icon           = rd.Icon,
                        HierarchyLevel = rd.Level,
                        IsSystemRole   = true,
                        TenantId       = tenantId
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
                    if (existing.TenantId != tenantId)
                    {
                        existing.TenantId = tenantId;
                    }
                    await roleManager.UpdateAsync(existing);
                }

                // Sync permission groups
                var roleEntity = await context.Roles.IgnoreQueryFilters()
                    .Include(r => r.PermissionGroups)
                    .FirstOrDefaultAsync(r => r.Id == existing.Id);

                if (roleEntity == null) continue;

                // Load groups for removal — need full nav
                var existingGroups = await context.AppRolePermissionGroups.IgnoreQueryFilters()
                    .Include(x => x.PermissionGroup)
                    .Where(x => x.RoleId == roleEntity.Id && x.TenantId == tenantId)
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
                        PermissionGroupId = pg.Id,
                        TenantId         = tenantId
                    });
                }
            }

            await context.SaveChangesAsync();

            // ── Legacy role cleanup: remove old "Manager", "Employee", "Project Coordinator" 
            //    (the seed used to use these names; now replaced by the above set)
            var legacyRoles = new[] { "Manager", "Employee" };
            foreach (var legacyName in legacyRoles)
            {
                var legacy = await roleManager.Roles.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Name == legacyName && r.TenantId == tenantId);
                if (legacy != null && !legacy.IsSystemRole)
                    await roleManager.DeleteAsync(legacy);
            }
        }

        // ── Super Admin User ──────────────────────────────────────────────────

        private static async Task SeedSuperAdminAsync(UserManager<User> userManager)
        {
            const string superAdminEmail = "superadmin@taskflow.com";
            var superAdmin = await userManager.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == superAdminEmail);

            if (superAdmin == null)
            {
                superAdmin = new User
                {
                    UserName       = superAdminEmail,
                    Email          = superAdminEmail,
                    FullName       = "Super Administrator",
                    JobTitle       = "Executive Director",
                    Department     = "Management",
                    EmailConfirmed = true,
                    TenantId       = "default-tenant-id"
                };

                var result = await userManager.CreateAsync(superAdmin, "SuperAdmin@TaskFlow!2026");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(superAdmin, "Super Admin");
                }
            }
            else
            {
                if (superAdmin.TenantId != "default-tenant-id")
                {
                    superAdmin.TenantId = "default-tenant-id";
                    await userManager.UpdateAsync(superAdmin);
                }
            }

            // Also keep the legacy admin@example.com as Admin role for backward compat
            const string legacyAdminEmail = "admin@example.com";
            var legacyAdmin = await userManager.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == legacyAdminEmail);
            if (legacyAdmin == null)
            {
                legacyAdmin = new User
                {
                    UserName       = legacyAdminEmail,
                    Email          = legacyAdminEmail,
                    FullName       = "Admin User",
                    JobTitle       = "System Administrator",
                    Department     = "IT",
                    EmailConfirmed = true,
                    TenantId       = "default-tenant-id"
                };
                var result = await userManager.CreateAsync(legacyAdmin, "YourDefaultAdminPassword@123!");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(legacyAdmin, "Admin");
            }
            else
            {
                if (legacyAdmin.TenantId != "default-tenant-id")
                {
                    legacyAdmin.TenantId = "default-tenant-id";
                    await userManager.UpdateAsync(legacyAdmin);
                }

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
            if (!context.Areas.IgnoreQueryFilters().Any())
            {
                context.Areas.AddRange(new List<Area>
                {
                    new Area { Name = "Web API", TenantId = "default-tenant-id" },
                    new Area { Name = "Frontend", TenantId = "default-tenant-id" },
                    new Area { Name = "Database", TenantId = "default-tenant-id" },
                    new Area { Name = "Mobile", TenantId = "default-tenant-id" },
                    new Area { Name = "Full-stack", TenantId = "default-tenant-id" }
                });
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedPublicHolidaysAsync(ApplicationDbContext context)
        {
            if (!context.PublicHolidays.IgnoreQueryFilters().Any())
            {
                context.PublicHolidays.AddRange(new List<PublicHoliday>
                {
                    new PublicHoliday { Name = "Independence Day", FromDate = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = true, TenantId = "default-tenant-id" },
                    new PublicHoliday { Name = "Victory Day",      FromDate = new DateTime(2026, 12, 16, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 12, 16, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = true, TenantId = "default-tenant-id" },
                    new PublicHoliday { Name = "May Day",          FromDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),  ToDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),  IsFixedDate = true, TenantId = "default-tenant-id" },
                    new PublicHoliday { Name = "Eid-ul-Fitr",      FromDate = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc), ToDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc), IsFixedDate = false, TenantId = "default-tenant-id" }
                });
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedSampleDataAsync(
            ApplicationDbContext context,
            UserManager<User> userManager,
            IServiceProvider serviceProvider)
        {
            if (await context.Projects.IgnoreQueryFilters().AnyAsync())
            {
                return; // Already seeded
            }

            var tenantProvider = serviceProvider.GetRequiredService<ITenantProvider>();
            var resourceService = serviceProvider.GetRequiredService<IResourceService>();
            var workflowEngine = serviceProvider.GetRequiredService<IWorkflowEngineService>();

            // Ensure we are in default tenant context
            tenantProvider.SetTenant("default-tenant-id");

            // 1. Seed users for TaskFlow Corp
            var pmUser = await CreateUserAsync(userManager, "pm@taskflow.com", "Patricia Miller", "Project Manager", "default-tenant-id", "Senior Portfolio Manager", "PMO");
            var leadUser = await CreateUserAsync(userManager, "lead@taskflow.com", "Luke Carter", "Project Lead", "default-tenant-id", "Technical Lead", "Engineering");
            var dev1 = await CreateUserAsync(userManager, "dev1@taskflow.com", "David Evans", "Developer", "default-tenant-id", "Senior Software Engineer", "Engineering");
            var dev2 = await CreateUserAsync(userManager, "dev2@taskflow.com", "Diana Prince", "Developer", "default-tenant-id", "Software Engineer II", "Engineering");
            var dev3 = await CreateUserAsync(userManager, "dev3@taskflow.com", "Devin Smith", "Developer", "default-tenant-id", "Associate Engineer", "Engineering");
            var qa1 = await CreateUserAsync(userManager, "qa1@taskflow.com", "Quincy Adams", "QA Engineer", "default-tenant-id", "QA Lead", "Quality Assurance");
            var qa2 = await CreateUserAsync(userManager, "qa2@taskflow.com", "Queen Latifa", "QA Engineer", "default-tenant-id", "QA Analyst", "Quality Assurance");
            var clientUser = await CreateUserAsync(userManager, "client@taskflow.com", "Charles Xavier", "Client", "default-tenant-id", "Product Owner", "External Stakeholders");

            // 2. Resource Profiles and Salaries
            // Patricia Miller (PM)
            var pmProfile = await resourceService.GetOrCreateProfileAsync(pmUser.Id);
            pmProfile.SeniorityLevel = SeniorityLevel.Senior;
            pmProfile.ResourceType = ResourceType.FullTime;
            pmProfile.Department = "PMO";
            context.Entry(pmProfile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(pmProfile.Id, SalaryType.MonthlySalary, 180000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // Luke Carter (Lead)
            var leadProfile = await resourceService.GetOrCreateProfileAsync(leadUser.Id);
            leadProfile.SeniorityLevel = SeniorityLevel.Senior;
            leadProfile.ResourceType = ResourceType.FullTime;
            leadProfile.Department = "Engineering";
            context.Entry(leadProfile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(leadProfile.Id, SalaryType.MonthlySalary, 160000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // David Evans (Senior Dev)
            var dev1Profile = await resourceService.GetOrCreateProfileAsync(dev1.Id);
            dev1Profile.SeniorityLevel = SeniorityLevel.Senior;
            dev1Profile.ResourceType = ResourceType.FullTime;
            dev1Profile.Department = "Engineering";
            context.Entry(dev1Profile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(dev1Profile.Id, SalaryType.MonthlySalary, 130000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // Diana Prince (Mid Dev)
            var dev2Profile = await resourceService.GetOrCreateProfileAsync(dev2.Id);
            dev2Profile.SeniorityLevel = SeniorityLevel.Mid;
            dev2Profile.ResourceType = ResourceType.FullTime;
            dev2Profile.Department = "Engineering";
            context.Entry(dev2Profile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(dev2Profile.Id, SalaryType.MonthlySalary, 90000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // Devin Smith (Junior Dev)
            var dev3Profile = await resourceService.GetOrCreateProfileAsync(dev3.Id);
            dev3Profile.SeniorityLevel = SeniorityLevel.Junior;
            dev3Profile.ResourceType = ResourceType.Contractual;
            dev3Profile.Department = "Engineering";
            context.Entry(dev3Profile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(dev3Profile.Id, SalaryType.HourlyRate, 400m, DateTime.UtcNow.AddMonths(-6), "Initial contract rate", pmUser.Id, billRate: 800m);

            // Quincy Adams (QA Lead)
            var qa1Profile = await resourceService.GetOrCreateProfileAsync(qa1.Id);
            qa1Profile.SeniorityLevel = SeniorityLevel.Senior;
            qa1Profile.ResourceType = ResourceType.FullTime;
            qa1Profile.Department = "Quality Assurance";
            context.Entry(qa1Profile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(qa1Profile.Id, SalaryType.MonthlySalary, 110000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // Queen Latifa (QA Analyst)
            var qa2Profile = await resourceService.GetOrCreateProfileAsync(qa2.Id);
            qa2Profile.SeniorityLevel = SeniorityLevel.Mid;
            qa2Profile.ResourceType = ResourceType.FullTime;
            qa2Profile.Department = "Quality Assurance";
            context.Entry(qa2Profile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(qa2Profile.Id, SalaryType.MonthlySalary, 75000m, DateTime.UtcNow.AddMonths(-12), "Annual salary", pmUser.Id);

            // Add Skills
            var skills = new List<ResourceSkill>
            {
                new ResourceSkill { ResourceProfileId = dev1Profile.Id, SkillName = "Web API", ProficiencyLevel = ProficiencyLevel.Expert, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = dev1Profile.Id, SkillName = "Database", ProficiencyLevel = ProficiencyLevel.Expert, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = dev2Profile.Id, SkillName = "Frontend", ProficiencyLevel = ProficiencyLevel.Expert, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = dev2Profile.Id, SkillName = "Full-stack", ProficiencyLevel = ProficiencyLevel.Intermediate, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = dev3Profile.Id, SkillName = "Mobile", ProficiencyLevel = ProficiencyLevel.Intermediate, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = dev3Profile.Id, SkillName = "Frontend", ProficiencyLevel = ProficiencyLevel.Intermediate, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = qa1Profile.Id, SkillName = "Quality Assurance", ProficiencyLevel = ProficiencyLevel.Expert, TenantId = "default-tenant-id" },
                new ResourceSkill { ResourceProfileId = qa2Profile.Id, SkillName = "Quality Assurance", ProficiencyLevel = ProficiencyLevel.Intermediate, TenantId = "default-tenant-id" }
            };
            context.ResourceSkills.AddRange(skills);
            await context.SaveChangesAsync();

            // Add leave/availability blocks
            var leaveBlock = new ResourceAvailabilityBlock
            {
                UserId = dev2.Id,
                ResourceProfileId = dev2Profile.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(5),
                EndDate = DateTime.UtcNow.Date.AddDays(7),
                Reason = AvailabilityBlockReason.Leave,
                Notes = "Going on family vacation",
                ApprovalStatus = LeaveApprovalStatus.Approved,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };
            context.ResourceAvailabilityBlocks.Add(leaveBlock);
            await context.SaveChangesAsync();

            // 3. Projects
            var project1 = new Project
            {
                Name = "Alpha Cloud Migration",
                Description = "Migrate legacy on-premise infrastructure and services to secure AWS cloud environments.",
                StrategicStatus = ProjectStrategicStatus.Active,
                StrategicStatusReason = "Crucial for Q3 infrastructure optimization and high-availability targets.",
                StrategicStatusChangedAt = DateTime.UtcNow.AddDays(-10),
                StrategicStatusChangedById = pmUser.Id,
                IsOnExecutiveRadar = true,
                RequiredSkills = "Web API, DevOps, Database",
                CreatedById = pmUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                BudgetMode = BudgetMode.PreApproved,
                ApprovedBudget = 5000000m,
                ContingencyReserve = 500000m,
                BudgetSetAt = DateTime.UtcNow.AddDays(-28),
                BudgetSetById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var project2 = new Project
            {
                Name = "Beta Mobile Redesign",
                Description = "Re-architect the user portal for iOS and Android platforms using modern hybrid frameworks.",
                StrategicStatus = ProjectStrategicStatus.Delayed,
                StrategicStatusReason = "Delays in mobile designer resource allocation and Figma specification lock-in.",
                StrategicStatusChangedAt = DateTime.UtcNow.AddDays(-5),
                StrategicStatusChangedById = pmUser.Id,
                IsOnExecutiveRadar = true,
                RequiredSkills = "Mobile, Frontend, UI/UX",
                CreatedById = pmUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                BudgetMode = BudgetMode.PreApproved,
                ApprovedBudget = 3500000m,
                BudgetSetAt = DateTime.UtcNow.AddDays(-18),
                BudgetSetById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var project3 = new Project
            {
                Name = "Gamma AI Recommendation Engine",
                Description = "Develop an advanced predictive recommendation algorithm to enhance user dashboard engagement.",
                StrategicStatus = ProjectStrategicStatus.Planning,
                PlannedStartWeek = 35,
                StrategicStatusReason = "Awaiting research phase validation and Python library licensing approvals.",
                StrategicStatusChangedAt = DateTime.UtcNow.AddDays(-2),
                StrategicStatusChangedById = pmUser.Id,
                IsOnExecutiveRadar = false,
                RequiredSkills = "AI/ML, Python, Web API",
                CreatedById = pmUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                BudgetMode = BudgetMode.NotSet,
                TenantId = "default-tenant-id"
            };

            var project4 = new Project
            {
                Name = "Delta Legacy Offboarding",
                Description = "Decommission outdated servers, migrate archive data, and clean up inactive databases.",
                StrategicStatus = ProjectStrategicStatus.OnHold,
                StrategicStatusReason = "Deferred due to higher priority resource allocations on Project Alpha Cloud.",
                StrategicStatusChangedAt = DateTime.UtcNow.AddDays(-1),
                StrategicStatusChangedById = pmUser.Id,
                IsOnExecutiveRadar = false,
                CreatedById = pmUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                BudgetMode = BudgetMode.NotSet,
                TenantId = "default-tenant-id"
            };

            var project5 = new Project
            {
                Name = "Omega Portal Release",
                Description = "The Q1 release covering new authentication portals and self-service dashboards.",
                StrategicStatus = ProjectStrategicStatus.Active, // completed status is represented as active with close updates
                StrategicStatusReason = "Successfully delivered and signed off by key client stakeholders.",
                StrategicStatusChangedAt = DateTime.UtcNow.AddDays(-15),
                StrategicStatusChangedById = pmUser.Id,
                IsOnExecutiveRadar = false,
                CreatedById = pmUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-60),
                BudgetMode = BudgetMode.NotSet,
                TenantId = "default-tenant-id"
            };

            context.Projects.AddRange(project1, project2, project3, project4, project5);
            await context.SaveChangesAsync();

            // Portfolio Decisions
            context.PortfolioDecisions.AddRange(new List<PortfolioDecision>
            {
                new PortfolioDecision
                {
                    ProjectId = project1.Id,
                    DecisionType = "PlanNewProject",
                    Notes = "Alpha Cloud Migration project approved to eliminate recurring on-prem hardware failure risks.",
                    MadeById = pmUser.Id,
                    MadeAt = DateTime.UtcNow.AddDays(-29),
                    TenantId = "default-tenant-id"
                },
                new PortfolioDecision
                {
                    ProjectId = project1.Id,
                    DecisionType = "PreApproved",
                    Notes = "Approved 5,000,000 BDT baseline budget with 500,000 BDT contingency reserve for hosting licenses.",
                    MadeById = pmUser.Id,
                    MadeAt = DateTime.UtcNow.AddDays(-28),
                    TenantId = "default-tenant-id"
                },
                new PortfolioDecision
                {
                    ProjectId = project2.Id,
                    DecisionType = "DelayProject",
                    Notes = "Project marked Delayed until dedicated UX designer resources are acquired.",
                    MadeById = pmUser.Id,
                    MadeAt = DateTime.UtcNow.AddDays(-5),
                    TenantId = "default-tenant-id"
                }
            });

            // Other Costs (Non-Labor)
            context.ProjectOtherCosts.AddRange(new List<ProjectOtherCost>
            {
                new ProjectOtherCost
                {
                    ProjectId = project1.Id,
                    Category = OtherCostCategory.Software,
                    Description = "AWS Enterprise Cloud Support Plan",
                    EstimatedAmount = 150000m,
                    Frequency = CostFrequency.Monthly,
                    PlannedDate = DateTime.UtcNow.Date.AddDays(-30),
                    CreatedById = pmUser.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-28),
                    TenantId = "default-tenant-id"
                },
                new ProjectOtherCost
                {
                    ProjectId = project1.Id,
                    Category = OtherCostCategory.Hardware,
                    Description = "Physical SAN Backup Drives",
                    EstimatedAmount = 300000m,
                    Frequency = CostFrequency.OneTime,
                    PlannedDate = DateTime.UtcNow.Date.AddDays(-15),
                    CreatedById = pmUser.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-28),
                    TenantId = "default-tenant-id"
                },
                new ProjectOtherCost
                {
                    ProjectId = project2.Id,
                    Category = OtherCostCategory.Travel,
                    Description = "On-site Client Kickoff travel",
                    EstimatedAmount = 75000m,
                    Frequency = CostFrequency.OneTime,
                    PlannedDate = DateTime.UtcNow.Date.AddDays(-10),
                    CreatedById = pmUser.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-18),
                    TenantId = "default-tenant-id"
                }
            });

            // Project Resource Allocations (for labor cost PV and heatmap calculations)
            context.ProjectResourceAllocations.AddRange(new List<ProjectResourceAllocation>
            {
                new ProjectResourceAllocation
                {
                    ProjectId = project1.Id,
                    UserId = dev1.Id,
                    ResourceProfileId = dev1Profile.Id,
                    AllocationPercentage = 100,
                    StartDate = DateTime.UtcNow.Date.AddDays(-15),
                    EndDate = DateTime.UtcNow.Date.AddDays(45),
                    ProjectRole = "Lead Infrastructure Architect",
                    AllocatedById = pmUser.Id,
                    AllocatedAt = DateTime.UtcNow.AddDays(-15),
                    TenantId = "default-tenant-id"
                },
                new ProjectResourceAllocation
                {
                    ProjectId = project1.Id,
                    UserId = dev2.Id,
                    ResourceProfileId = dev2Profile.Id,
                    AllocationPercentage = 50,
                    StartDate = DateTime.UtcNow.Date.AddDays(-15),
                    EndDate = DateTime.UtcNow.Date.AddDays(45),
                    ProjectRole = "Cloud Engineer",
                    AllocatedById = pmUser.Id,
                    AllocatedAt = DateTime.UtcNow.AddDays(-15),
                    TenantId = "default-tenant-id"
                },
                new ProjectResourceAllocation
                {
                    ProjectId = project2.Id,
                    UserId = dev2.Id,
                    ResourceProfileId = dev2Profile.Id,
                    AllocationPercentage = 50,
                    StartDate = DateTime.UtcNow.Date.AddDays(-10),
                    EndDate = DateTime.UtcNow.Date.AddDays(30),
                    ProjectRole = "Frontend Developer",
                    AllocatedById = pmUser.Id,
                    AllocatedAt = DateTime.UtcNow.AddDays(-10),
                    TenantId = "default-tenant-id"
                },
                new ProjectResourceAllocation
                {
                    ProjectId = project1.Id,
                    UserId = qa1.Id,
                    ResourceProfileId = qa1Profile.Id,
                    AllocationPercentage = 100,
                    StartDate = DateTime.UtcNow.Date.AddDays(-10),
                    EndDate = DateTime.UtcNow.Date.AddDays(45),
                    ProjectRole = "QA Automation Lead",
                    AllocatedById = pmUser.Id,
                    AllocatedAt = DateTime.UtcNow.AddDays(-10),
                    TenantId = "default-tenant-id"
                }
            });
            await context.SaveChangesAsync();

            // Sprints for Alpha Cloud Migration
            var sprint1 = new Sprint
            {
                ProjectId = project1.Id,
                Name = "Sprint 1: AWS Foundation Setup",
                StartDate = DateTime.UtcNow.Date.AddDays(-28),
                EndDate = DateTime.UtcNow.Date.AddDays(-15),
                IsActive = false,
                TenantId = "default-tenant-id"
            };

            var sprint2 = new Sprint
            {
                ProjectId = project1.Id,
                Name = "Sprint 2: Database Migration & Pipelines",
                StartDate = DateTime.UtcNow.Date.AddDays(-14),
                EndDate = DateTime.UtcNow.Date.AddDays(0), // Active
                IsActive = true,
                TenantId = "default-tenant-id"
            };

            var sprint3 = new Sprint
            {
                ProjectId = project1.Id,
                Name = "Sprint 3: API Service Deployment",
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(14),
                IsActive = false,
                TenantId = "default-tenant-id"
            };

            context.Sprints.AddRange(sprint1, sprint2, sprint3);
            await context.SaveChangesAsync();

            // Epics
            var epic1 = new Epic
            {
                ProjectId = project1.Id,
                Name = "Cloud Infrastructure & VPC Setup",
                Description = "Establish secure network topologies, gateways, subnets, and AWS accounts.",
                TenantId = "default-tenant-id"
            };

            var epic2 = new Epic
            {
                ProjectId = project1.Id,
                Name = "Database Migration & Schema Sync",
                Description = "Establish RDS instances, synchronize schemas, and perform secure data migrations.",
                TenantId = "default-tenant-id"
            };
            context.Epics.AddRange(epic1, epic2);
            await context.SaveChangesAsync();

            // Features
            var feature1 = new Feature
            {
                EpicId = epic1.Id,
                Name = "VPC Topology & Route Tables",
                Description = "Configure public/private subnets and route mappings.",
                TenantId = "default-tenant-id"
            };

            var feature2 = new Feature
            {
                EpicId = epic2.Id,
                Name = "PostgreSQL RDS Setup",
                Description = "Deploy highly available RDS instances and secure parameter groups.",
                TenantId = "default-tenant-id"
            };
            context.Features.AddRange(feature1, feature2);
            await context.SaveChangesAsync();

            // User Stories
            var userStory1 = new UserStory
            {
                FeatureId = feature1.Id,
                Title = "Deploy infrastructure via Terraform templates",
                Description = "As a Cloud Engineer, I want to deploy the VPC architecture automatically using Terraform to ensure repeatability.",
                AcceptanceCriteria = "Terraform applies with zero errors; VPC contains 2 public and 2 private subnets.",
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var userStory2 = new UserStory
            {
                FeatureId = feature2.Id,
                Title = "RDS replica configuration & failover testing",
                Description = "As a Database Engineer, I want RDS configured with a multi-AZ replica so that failover completes in under 60 seconds.",
                AcceptanceCriteria = "Primary failover triggered manually completes and app reconnects automatically.",
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };
            context.UserStories.AddRange(userStory1, userStory2);
            await context.SaveChangesAsync();

            // Test Cases
            var testCase1 = new TestCase
            {
                UserStoryId = userStory1.Id,
                Title = "Verify Terraform syntax validation",
                Steps = "1. cd terraform/\n2. terraform init\n3. terraform validate",
                ExpectedResult = "Success, clean output.",
                IsPassed = true,
                TenantId = "default-tenant-id"
            };

            var testCase2 = new TestCase
            {
                UserStoryId = userStory2.Id,
                Title = "Trigger manual RDS failover",
                Steps = "1. Navigate to AWS Console.\n2. Trigger reboot with failover on postgres-db.\n3. Measure application downtime.",
                ExpectedResult = "Application reconnects within 30 seconds; no lost data.",
                IsPassed = true,
                TenantId = "default-tenant-id"
            };
            context.TestCases.AddRange(testCase1, testCase2);
            await context.SaveChangesAsync();

            // 4. Workflow Template
            var wt = new WorkflowTemplate
            {
                Name = "Standard Dev Pipeline",
                Description = "Standard 5-stage pipeline with Development, Review, and QA Gates.",
                IsActive = true,
                TenantId = "default-tenant-id"
            };
            context.WorkflowTemplates.Add(wt);
            await context.SaveChangesAsync();

            var devRole = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Name == "Developer");
            var qaRole = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Name == "QA Engineer");
            var leadRole = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Name == "Project Lead");

            var stages = new List<WorkflowStage>
            {
                new WorkflowStage
                {
                    WorkflowTemplateId = wt.Id,
                    Name = "Design & Spec",
                    Order = 1,
                    GateType = StageGateType.None,
                    DefaultRoleTitle = "System Analyst",
                    DependencyType = StageDependency.FinishToStart,
                    LagHours = 0,
                    DefinitionOfDone = "Create architecture diagrams and upload specs.",
                    TenantId = "default-tenant-id"
                },
                new WorkflowStage
                {
                    WorkflowTemplateId = wt.Id,
                    Name = "Development",
                    Order = 2,
                    GateType = StageGateType.CommittedWithHours,
                    DefaultRoleTitle = "Developer",
                    RoleId = devRole?.Id,
                    DependencyType = StageDependency.FinishToStart,
                    LagHours = 0,
                    DefinitionOfDone = "Status set to Committed and actual hours must be logged.",
                    TenantId = "default-tenant-id"
                },
                new WorkflowStage
                {
                    WorkflowTemplateId = wt.Id,
                    Name = "Peer Code Review",
                    Order = 3,
                    GateType = StageGateType.CommittedWithPeerReview,
                    DefaultRoleTitle = "Tech Lead",
                    RoleId = leadRole?.Id,
                    DependencyType = StageDependency.FinishToStart,
                    LagHours = 2,
                    DefinitionOfDone = "Status set to Committed and at least 1 reviewer comment added.",
                    TenantId = "default-tenant-id"
                },
                new WorkflowStage
                {
                    WorkflowTemplateId = wt.Id,
                    Name = "QA Testing",
                    Order = 4,
                    GateType = StageGateType.TestedWithAllCasesPassed,
                    DefaultRoleTitle = "QA Engineer",
                    RoleId = qaRole?.Id,
                    DependencyType = StageDependency.FinishToStart,
                    LagHours = 0,
                    DefinitionOfDone = "Status set to Tested and all linked test cases must pass.",
                    TenantId = "default-tenant-id"
                },
                new WorkflowStage
                {
                    WorkflowTemplateId = wt.Id,
                    Name = "UAT & Release Signoff",
                    Order = 5,
                    GateType = StageGateType.None,
                    DefaultRoleTitle = "Project Lead",
                    RoleId = leadRole?.Id,
                    DependencyType = StageDependency.FinishToStart,
                    LagHours = 0,
                    RequiresAccountableSignoff = true,
                    DefinitionOfDone = "Requires PM / Accountable Lead signoff to close.",
                    TenantId = "default-tenant-id"
                }
            };
            context.WorkflowStages.AddRange(stages);
            await context.SaveChangesAsync();

            // Work Package Parent Task
            var parentTask = new TaskItem
            {
                Title = "Deploy PostgreSQL RDS High-Availability Cluster",
                Description = "Complete setup and failover automation for PostgreSQL RDS deployment.",
                Status = Models.Enums.TaskStatus.New,
                Type = TaskType.NewRequest,
                Priority = TaskPriority.High,
                ProjectId = project1.Id,
                SprintId = sprint2.Id,
                EpicId = epic2.Id,
                FeatureId = feature2.Id,
                UserStoryId = userStory2.Id,
                CreatedById = pmUser.Id,
                AccountableUserId = pmUser.Id,
                IsBacklog = false,
                TenantId = "default-tenant-id"
            };
            context.Tasks.Add(parentTask);
            await context.SaveChangesAsync();

            // Spawn Workflow stages using WorkflowEngineService
            await workflowEngine.SpawnWorkflowSubTasksAsync(parentTask.Id, wt.Id);

            // Fetch spawned tasks and update progress
            var subTasks = await context.Tasks
                .Include(t => t.WorkflowStage)
                .Where(t => t.ParentTaskId == parentTask.Id)
                .OrderBy(t => t.WorkflowStage!.Order)
                .ToListAsync();

            if (subTasks.Count >= 5)
            {
                // S1: Design & Spec (Done)
                var s1 = subTasks[0];
                s1.AssigneeId = leadUser.Id;
                s1.EstimatedOptimisticHours = 4;
                s1.EstimatedMostLikelyHours = 6;
                s1.EstimatedPessimisticHours = 12;
                s1.PertEstimatedHours = workflowEngine.CalculatePert(4, 6, 12);
                s1.ActualHours = 7;
                s1.Status = Models.Enums.TaskStatus.Done;
                s1.CompletedAt = DateTime.UtcNow.AddDays(-5);
                context.Update(s1);

                // S2: Development (InProgress)
                var s2 = subTasks[1];
                s2.AssigneeId = dev1.Id;
                s2.EstimatedOptimisticHours = 8;
                s2.EstimatedMostLikelyHours = 12;
                s2.EstimatedPessimisticHours = 24;
                s2.PertEstimatedHours = workflowEngine.CalculatePert(8, 12, 24);
                s2.Status = Models.Enums.TaskStatus.InProgress;
                s2.StartDate = DateTime.UtcNow.AddDays(-3);
                context.Update(s2);

                // S3: Peer Code Review (Pre-assign, New)
                var s3 = subTasks[2];
                s3.AssigneeId = leadUser.Id;
                s3.EstimatedOptimisticHours = 2;
                s3.EstimatedMostLikelyHours = 3;
                s3.EstimatedPessimisticHours = 6;
                s3.PertEstimatedHours = workflowEngine.CalculatePert(2, 3, 6);
                context.Update(s3);

                // S4: QA Testing (Pre-assign, New)
                var s4 = subTasks[3];
                s4.AssigneeId = qa2.Id;
                s4.EstimatedOptimisticHours = 3;
                s4.EstimatedMostLikelyHours = 4;
                s4.EstimatedPessimisticHours = 8;
                s4.PertEstimatedHours = workflowEngine.CalculatePert(3, 4, 8);
                context.Update(s4);

                // S5: UAT & Release (Pre-assign, New)
                var s5 = subTasks[4];
                s5.AssigneeId = leadUser.Id;
                s5.EstimatedOptimisticHours = 2;
                s5.EstimatedMostLikelyHours = 3;
                s5.EstimatedPessimisticHours = 6;
                s5.PertEstimatedHours = workflowEngine.CalculatePert(2, 3, 6);
                context.Update(s5);

                await context.SaveChangesAsync();

                // Re-sync parent status and hours
                await workflowEngine.SyncParentStatusAsync(parentTask.Id, pmUser.Id);
            }

            // Standalone completed tasks in Sprint 1
            var taskCompleted1 = new TaskItem
            {
                Title = "Establish AWS Organization and Root Accounts",
                Description = "Complete initial AWS root account config and configure MFA.",
                Status = Models.Enums.TaskStatus.Done,
                Type = TaskType.Activity,
                Priority = TaskPriority.Medium,
                ProjectId = project1.Id,
                SprintId = sprint1.Id,
                AssigneeId = dev1.Id,
                EstimatedHours = 8,
                ActualHours = 10,
                CreatedById = pmUser.Id,
                CompletedAt = DateTime.UtcNow.AddDays(-20),
                TenantId = "default-tenant-id"
            };

            var taskCompleted2 = new TaskItem
            {
                Title = "Draft Cloud Network Security Policies",
                Description = "Compile PMP compliance documentation for network boundaries.",
                Status = Models.Enums.TaskStatus.Done,
                Type = TaskType.Enhancement,
                Priority = TaskPriority.Low,
                ProjectId = project1.Id,
                SprintId = sprint1.Id,
                AssigneeId = leadUser.Id,
                EstimatedHours = 4,
                ActualHours = 3.5m,
                CreatedById = pmUser.Id,
                CompletedAt = DateTime.UtcNow.AddDays(-18),
                TenantId = "default-tenant-id"
            };

            context.Tasks.AddRange(taskCompleted1, taskCompleted2);

            // Standalone tasks in Sprint 2 (Active)
            var taskToDo = new TaskItem
            {
                Title = "Configure IAM Users and Policy Groups",
                Description = "Define role-based read/write access policies for development teams.",
                Status = Models.Enums.TaskStatus.ToDo,
                Type = TaskType.Activity,
                Priority = TaskPriority.Medium,
                ProjectId = project1.Id,
                SprintId = sprint2.Id,
                AssigneeId = dev2.Id,
                EstimatedHours = 6,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var taskPaused = new TaskItem
            {
                Title = "Configure SSL Certs on AWS Load Balancer",
                Description = "Pending domain validation from client IT side.",
                Status = Models.Enums.TaskStatus.InProgress,
                Type = TaskType.Activity,
                Priority = TaskPriority.High,
                ProjectId = project1.Id,
                SprintId = sprint2.Id,
                AssigneeId = dev1.Id,
                EstimatedHours = 4,
                IsPaused = true,
                PauseReason = "Waiting for Client IT department to share DNS TXT records.",
                PausedAt = DateTime.UtcNow.AddDays(-2),
                PausedById = pmUser.Id,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var taskTested = new TaskItem
            {
                Title = "VPC Peering connection to Legacy DB subnet",
                Description = "Create peering connection and configure routing/firewalls.",
                Status = Models.Enums.TaskStatus.Tested,
                Type = TaskType.Activity,
                Priority = TaskPriority.High,
                ProjectId = project1.Id,
                SprintId = sprint2.Id,
                AssigneeId = dev1.Id,
                EstimatedHours = 12,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            context.Tasks.AddRange(taskToDo, taskPaused, taskTested);

            // Backlog tasks
            var backlogTask1 = new TaskItem
            {
                Title = "Establish Disaster Recovery backup strategy",
                Description = "PMP Chapter 11 Risk management plan for RDS failover replication.",
                Status = Models.Enums.TaskStatus.New,
                Type = TaskType.Enhancement,
                Priority = TaskPriority.High,
                ProjectId = project1.Id,
                IsBacklog = true,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            var backlogTask2 = new TaskItem
            {
                Title = "Perform AWS cost billing dashboard optimization",
                Description = "Create billing alert rules and resource tag constraints.",
                Status = Models.Enums.TaskStatus.New,
                Type = TaskType.Activity,
                Priority = TaskPriority.Low,
                ProjectId = project1.Id,
                IsBacklog = true,
                CreatedById = pmUser.Id,
                TenantId = "default-tenant-id"
            };

            context.Tasks.AddRange(backlogTask1, backlogTask2);
            await context.SaveChangesAsync();

            // Task Comments
            context.TaskComments.AddRange(new List<TaskComment>
            {
                new TaskComment
                {
                    TaskId = taskPaused.Id,
                    UserId = dev1.Id,
                    CommentText = "I sent a follow up mail to client IT contact. Hope they provide the DNS record soon.",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    TenantId = "default-tenant-id"
                },
                new TaskComment
                {
                    TaskId = taskPaused.Id,
                    UserId = pmUser.Id,
                    CommentText = "Understood. Pausing this task for now to prevent resource overallocation.",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    TenantId = "default-tenant-id"
                }
            });
            await context.SaveChangesAsync();

            // ── 5. Acme Tenant Data (Isolation Verification) ─────────────────
            tenantProvider.SetTenant("acme-tenant-id");

            // Seed reference permissions and roles inside the Acme tenant first
            await SeedPermissionGroupsAsync(context);
            var roleManager = serviceProvider.GetRequiredService<RoleManager<AppRole>>();
            await SeedRolesAsync(roleManager, context);

            var acmeAdmin = await CreateUserAsync(userManager, "acmeadmin@acme.com", "Acme Admin User", "Admin", "acme-tenant-id", "IT Administrator", "IT");
            var acmeDev = await CreateUserAsync(userManager, "acmedev@acme.com", "Acme Developer", "Developer", "acme-tenant-id", "Software Engineer", "Product");

            var acmeDevProfile = await resourceService.GetOrCreateProfileAsync(acmeDev.Id);
            acmeDevProfile.SeniorityLevel = SeniorityLevel.Mid;
            acmeDevProfile.ResourceType = ResourceType.FullTime;
            acmeDevProfile.Department = "Product";
            context.Entry(acmeDevProfile).State = EntityState.Modified;
            await context.SaveChangesAsync();
            await resourceService.RecordSalaryChangeAsync(acmeDevProfile.Id, SalaryType.MonthlySalary, 100000m, DateTime.UtcNow.AddMonths(-3), "Acme hire", acmeAdmin.Id);

            var acmeProject = new Project
            {
                Name = "Acme Web Portal Setup",
                Description = "Configure initial public website and developer docs.",
                StrategicStatus = ProjectStrategicStatus.Active,
                CreatedById = acmeAdmin.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                BudgetMode = BudgetMode.PreApproved,
                ApprovedBudget = 1000000m,
                BudgetSetAt = DateTime.UtcNow.AddDays(-8),
                BudgetSetById = acmeAdmin.Id,
                TenantId = "acme-tenant-id"
            };
            context.Projects.Add(acmeProject);
            await context.SaveChangesAsync();

            var acmeSprint = new Sprint
            {
                ProjectId = acmeProject.Id,
                Name = "Acme Sprint 1: Setup",
                StartDate = DateTime.UtcNow.Date.AddDays(-5),
                EndDate = DateTime.UtcNow.Date.AddDays(9),
                IsActive = true,
                TenantId = "acme-tenant-id"
            };
            context.Sprints.Add(acmeSprint);
            await context.SaveChangesAsync();

            var acmeTask = new TaskItem
            {
                Title = "Acme Web Landing Page Mockup",
                Description = "Design a basic static page layout using Figma.",
                Status = Models.Enums.TaskStatus.InProgress,
                Type = TaskType.Activity,
                Priority = TaskPriority.Medium,
                ProjectId = acmeProject.Id,
                SprintId = acmeSprint.Id,
                AssigneeId = acmeDev.Id,
                EstimatedHours = 15,
                CreatedById = acmeAdmin.Id,
                TenantId = "acme-tenant-id"
            };
            context.Tasks.Add(acmeTask);
            await context.SaveChangesAsync();

            // Set tenant context back to default for safety
            tenantProvider.SetTenant("default-tenant-id");
        }

        private static async Task<User> CreateUserAsync(
            UserManager<User> userManager,
            string email,
            string fullName,
            string role,
            string tenantId,
            string jobTitle,
            string department)
        {
            var user = await userManager.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenantId);

            if (user == null)
            {
                user = new User
                {
                    UserName       = email,
                    Email          = email,
                    FullName       = fullName,
                    JobTitle       = jobTitle,
                    Department     = department,
                    EmailConfirmed = true,
                    TenantId       = tenantId
                };

                var result = await userManager.CreateAsync(user, "Password@123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, role);
                }
            }

            return user;
        }
    }
}
