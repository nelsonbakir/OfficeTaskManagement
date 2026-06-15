using System.Linq;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services;

namespace OfficeTaskManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, AppRole, string>
    {
        private readonly ITenantProvider _tenantProvider;

        public string CurrentTenantId => _tenantProvider.TenantId;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantProvider? tenantProvider = null)
            : base(options)
        {
            _tenantProvider = tenantProvider ?? new TestTenantProvider();
        }

        private class TestTenantProvider : ITenantProvider
        {
            private string _tenantId = "test-tenant";
            public string TenantId => _tenantId;
            public void SetTenant(string tenantId) => _tenantId = tenantId;
        }

        public DbSet<Tenant> Tenants { get; set; }

        public DbSet<Project> Projects { get; set; }
        public DbSet<Epic> Epics { get; set; }
        public DbSet<Feature> Features { get; set; }
        public DbSet<Sprint> Sprints { get; set; }
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<TaskHistory> TaskHistories { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<TaskComment> TaskComments { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Area> Areas { get; set; }
        public DbSet<UserStory> UserStories { get; set; }
        public DbSet<TestCase> TestCases { get; set; }
        public DbSet<PortfolioDecision> PortfolioDecisions { get; set; }

        // ── Workflow Engine (RACI) ───────────────────────────────────────────
        public DbSet<WorkflowTemplate> WorkflowTemplates { get; set; }
        public DbSet<WorkflowStage> WorkflowStages { get; set; }
        // ────────────────────────────────────────────────────────────────────

        // ── Dynamic Role & Permission System ─────────────────────────────────
        public DbSet<PermissionGroup> PermissionGroups { get; set; }
        public DbSet<PermissionGroupKey> PermissionGroupKeys { get; set; }
        public DbSet<AppRolePermissionGroup> AppRolePermissionGroups { get; set; }
        // ────────────────────────────────────────────────────────────────────

        // ── Resource Management ──────────────────────────────────────────────
        public DbSet<ResourceProfile> ResourceProfiles { get; set; }
        public DbSet<ResourceSkill> ResourceSkills { get; set; }
        public DbSet<ProjectResourceAllocation> ProjectResourceAllocations { get; set; }
        public DbSet<ResourceAvailabilityBlock> ResourceAvailabilityBlocks { get; set; }
        public DbSet<PublicHoliday> PublicHolidays { get; set; }
        public DbSet<SalaryHistory> SalaryHistories { get; set; }

        // ── Budget Management ───────────────────────────────────────
        public DbSet<ProjectOtherCost> ProjectOtherCosts { get; set; }
        // ───────────────────────────────────────────────────────────

        // ── AI Agent Tables ──────────────────────────────────────────────────
        public DbSet<CodeEmbedding>       CodeEmbeddings     { get; set; }
        public DbSet<AgentConversation>   AgentConversations { get; set; }
        public DbSet<AiEstimationLog>     AiEstimationLogs   { get; set; }
        // ────────────────────────────────────────────────────────────────────

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Delete Behaviors to avoid multiple cascade paths

            builder.Entity<Epic>()
                .HasOne(e => e.Project)
                .WithMany(p => p.Epics)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Feature>()
                .HasOne(f => f.Epic)
                .WithMany(e => e.Features)
                .HasForeignKey(f => f.EpicId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TaskItem>()
                .HasOne(t => t.Feature)
                .WithMany(f => f.Tasks)
                .HasForeignKey(t => t.FeatureId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<TaskItem>()
                .HasOne(t => t.Assignee)
                .WithMany()
                .HasForeignKey(t => t.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<TaskItem>()
                .HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<TaskItem>()
                .HasOne(t => t.Sprint)
                .WithMany(s => s.Tasks)
                .HasForeignKey(t => t.SprintId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TaskItem>()
                .HasOne(t => t.Project)
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Restrict); // Project deletion should not automatically cascade to tasks if sprint cascades? Or restrict here and cascade via Sprint.

            builder.Entity<TaskItem>()
                .HasOne(t => t.ParentTask)
                .WithMany(t => t.SubTasks)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.Cascade); // C4 fix: cascade-delete sub-tasks when parent is deleted

            builder.Entity<Sprint>()
                .HasOne(s => s.Project)
                .WithMany(p => p.Sprints)
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TaskHistory>()
                .HasOne(th => th.TaskItem)
                .WithMany(t => t.History)
                .HasForeignKey(th => th.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TaskHistory>()
                .HasOne(th => th.ChangedBy)
                .WithMany()
                .HasForeignKey(th => th.ChangedById)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.TaskItem)
                .WithMany(t => t.Attachments)
                .HasForeignKey(ta => ta.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.Project)
                .WithMany(p => p.Attachments)
                .HasForeignKey(ta => ta.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.Epic)
                .WithMany(e => e.Attachments)
                .HasForeignKey(ta => ta.EpicId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.Feature)
                .WithMany(f => f.Attachments)
                .HasForeignKey(ta => ta.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.UserStory)
                .WithMany(us => us.Attachments)
                .HasForeignKey(ta => ta.UserStoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.TestCase)
                .WithMany(tc => tc.Attachments)
                .HasForeignKey(ta => ta.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Attachment>()
                .HasOne(ta => ta.UploadedBy)
                .WithMany()
                .HasForeignKey(ta => ta.UploadedById)
                .OnDelete(DeleteBehavior.SetNull);
            // TaskComment relations
            builder.Entity<TaskComment>()
                .HasOne(tc => tc.Task)
                .WithMany(t => t.Comments)
                .HasForeignKey(tc => tc.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TaskComment>()
                .HasOne(tc => tc.User)
                .WithMany()
                .HasForeignKey(tc => tc.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Areas many-to-many
            builder.Entity<TaskItem>()
                .HasMany(t => t.Areas)
                .WithMany(a => a.Tasks)
                .UsingEntity(j => j.ToTable("TaskAreas"));

            // Configure UserStories
            builder.Entity<UserStory>()
                .HasOne(us => us.Feature)
                .WithMany(f => f.UserStories)
                .HasForeignKey(us => us.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserStory>()
                .HasOne(us => us.CreatedBy)
                .WithMany()
                .HasForeignKey(us => us.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure TestCases
            builder.Entity<TestCase>()
                .HasOne(tc => tc.UserStory)
                .WithMany(us => us.TestCases)
                .HasForeignKey(tc => tc.UserStoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure TaskItem UserStory relationship
            builder.Entity<TaskItem>()
                .HasOne(t => t.UserStory)
                .WithMany(us => us.Tasks)
                .HasForeignKey(t => t.UserStoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure TaskItem PausedBy relationship
            builder.Entity<TaskItem>()
                .HasOne(t => t.PausedBy)
                .WithMany()
                .HasForeignKey(t => t.PausedById)
                .OnDelete(DeleteBehavior.SetNull);

            // ── RACI Workflow Engine Relationships ───────────────────────────

            // TaskItem → WorkflowStage (the stage this sub-task represents)
            builder.Entity<TaskItem>()
                .HasOne(t => t.WorkflowStage)
                .WithMany()
                .HasForeignKey(t => t.WorkflowStageId)
                .OnDelete(DeleteBehavior.SetNull);

            // TaskItem → AccountableUser (the A in RACI — fixed for the work package lifetime)
            builder.Entity<TaskItem>()
                .HasOne(t => t.AccountableUser)
                .WithMany()
                .HasForeignKey(t => t.AccountableUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // WorkflowTemplate → Project (Restrict: deactivate templates before deleting project
            // to prevent orphaned sub-tasks that lose their stage definition context)
            builder.Entity<WorkflowTemplate>()
                .HasOne(wt => wt.Project)
                .WithMany()
                .HasForeignKey(wt => wt.ProjectId)
                .OnDelete(DeleteBehavior.Restrict); // L3 fix: was Cascade

            // WorkflowStage → WorkflowTemplate
            builder.Entity<WorkflowStage>()
                .HasOne(ws => ws.WorkflowTemplate)
                .WithMany(wt => wt.Stages)
                .HasForeignKey(ws => ws.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // WorkflowStage → AppRole (Dynamic Role)
            builder.Entity<WorkflowStage>()
                .HasOne(ws => ws.Role)
                .WithMany()
                .HasForeignKey(ws => ws.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            // ────────────────────────────────────────────────────────────────

            // Configure Project StrategicStatusChangedBy relationship
            builder.Entity<Project>()
                .HasOne(p => p.StrategicStatusChangedBy)
                .WithMany()
                .HasForeignKey(p => p.StrategicStatusChangedById)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure PortfolioDecision relationships
            builder.Entity<PortfolioDecision>()
                .HasOne(pd => pd.Project)
                .WithMany(p => p.PortfolioDecisions)
                .HasForeignKey(pd => pd.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PortfolioDecision>()
                .HasOne(pd => pd.MadeBy)
                .WithMany()
                .HasForeignKey(pd => pd.MadeById)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Budget Management Relationships ─────────────────────────────

            // Project.BudgetSetBy (SetNull: manager deletion does not affect budget record)
            builder.Entity<Project>()
                .HasOne(p => p.BudgetSetBy)
                .WithMany()
                .HasForeignKey(p => p.BudgetSetById)
                .OnDelete(DeleteBehavior.SetNull);

            // ProjectOtherCost → Project (Cascade: deleting a project removes its cost line items)
            builder.Entity<ProjectOtherCost>()
                .HasOne(oc => oc.Project)
                .WithMany(p => p.OtherCosts)
                .HasForeignKey(oc => oc.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // ProjectOtherCost → CreatedBy (SetNull: preserve cost records if user is removed)
            builder.Entity<ProjectOtherCost>()
                .HasOne(oc => oc.CreatedBy)
                .WithMany()
                .HasForeignKey(oc => oc.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            // ────────────────────────────────────────────────────────────────

            // ── Resource Management Relationships ────────────────────────────

            // ResourceProfile: 1-to-1 with User
            builder.Entity<ResourceProfile>()
                .HasOne(rp => rp.User)
                .WithOne(u => u.ResourceProfile)
                .HasForeignKey<ResourceProfile>(rp => rp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ResourceSkill: many-to-1 with ResourceProfile
            builder.Entity<ResourceSkill>()
                .HasOne(rs => rs.ResourceProfile)
                .WithMany(rp => rp.Skills)
                .HasForeignKey(rs => rs.ResourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // ProjectResourceAllocation: many-to-1 with Project
            builder.Entity<ProjectResourceAllocation>()
                .HasOne(pra => pra.Project)
                .WithMany(p => p.ResourceAllocations)
                .HasForeignKey(pra => pra.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // ProjectResourceAllocation: many-to-1 with User (the allocated person)
            builder.Entity<ProjectResourceAllocation>()
                .HasOne(pra => pra.User)
                .WithMany(u => u.ProjectAllocations)
                .HasForeignKey(pra => pra.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ProjectResourceAllocation: many-to-1 with AllocatedBy (manager)
            builder.Entity<ProjectResourceAllocation>()
                .HasOne(pra => pra.AllocatedBy)
                .WithMany()
                .HasForeignKey(pra => pra.AllocatedById)
                .OnDelete(DeleteBehavior.SetNull);

            // ProjectResourceAllocation: optional link to ResourceProfile
            builder.Entity<ProjectResourceAllocation>()
                .HasOne(pra => pra.ResourceProfile)
                .WithMany(rp => rp.ProjectAllocations)
                .HasForeignKey(pra => pra.ResourceProfileId)
                .OnDelete(DeleteBehavior.SetNull);

            // ResourceAvailabilityBlock: many-to-1 with User
            builder.Entity<ResourceAvailabilityBlock>()
                .HasOne(rab => rab.User)
                .WithMany(u => u.AvailabilityBlocks)
                .HasForeignKey(rab => rab.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ResourceAvailabilityBlock: CreatedBy (manager) — restrict delete
            builder.Entity<ResourceAvailabilityBlock>()
                .HasOne(rab => rab.CreatedBy)
                .WithMany()
                .HasForeignKey(rab => rab.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            // ResourceAvailabilityBlock: optional link to ResourceProfile
            builder.Entity<ResourceAvailabilityBlock>()
                .HasOne(rab => rab.ResourceProfile)
                .WithMany(rp => rp.AvailabilityBlocks)
                .HasForeignKey(rab => rab.ResourceProfileId)
                .OnDelete(DeleteBehavior.SetNull);

            // ────────────────────────────────────────────────────────────────

            // ── SalaryHistory Relationships ──────────────────────────────────

            // SalaryHistory → ResourceProfile (Restrict: history must not be
            // cascade-deleted; deactivate/archive profile first)
            builder.Entity<SalaryHistory>()
                .HasOne(sh => sh.ResourceProfile)
                .WithMany(rp => rp.SalaryHistories)
                .HasForeignKey(sh => sh.ResourceProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // SalaryHistory → RecordedBy User (SetNull so deleting a manager
            // does not delete the salary audit trail)
            builder.Entity<SalaryHistory>()
                .HasOne(sh => sh.RecordedBy)
                .WithMany()
                .HasForeignKey(sh => sh.RecordedById)
                .OnDelete(DeleteBehavior.SetNull);

            // Enforce uniqueness: only one active record (EffectiveTo IS NULL)
            // per ResourceProfile at the DB level via a filtered unique index.
            builder.Entity<SalaryHistory>()
                .HasIndex(sh => new { sh.ResourceProfileId, sh.EffectiveTo })
                .HasFilter("\"EffectiveTo\" IS NULL")
                .IsUnique()
                .HasDatabaseName("UIX_SalaryHistory_OneActivePerProfile");

            // ────────────────────────────────────────────────────────────────

            // ── AI Agent Entity Configurations ───────────────────────────────

            // CodeEmbedding — pgvector index + SQLite dev compatibility
            builder.Entity<CodeEmbedding>(e =>
            {
                e.HasIndex(x => x.FilePath);
                e.HasIndex(x => x.FileHash);
                e.HasIndex(x => x.TenantId);
                // IVFFlat index defined in raw SQL migration (pgvector-only)
            });

            // CodeEmbedding.Embedding — float[] is not a primitive EF Core type.
            // Always map via JSON string conversion. At runtime with PostgreSQL:
            //   - The [Column(TypeName = "vector(768)")] attribute tells Npgsql to use pgvector type
            //   - The conversion is overridden by Pgvector EF Core extension when available
            // For design-time tools and SQLite dev: always store as TEXT (JSON float array)
            var floatArrayComparer = new ValueComparer<float[]>(
                (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToArray()
            );

            builder.Entity<CodeEmbedding>()
                .Property(e => e.Embedding)
                .HasColumnType("TEXT")  // Overridden to vector(768) in migration for PostgreSQL
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<float[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<float>()
                )
                .Metadata.SetValueComparer(floatArrayComparer);

            // AgentConversation — expire index for cleanup job
            builder.Entity<AgentConversation>(e =>
            {
                e.HasIndex(x => new { x.UserId, x.EntityType, x.EntityId });
                e.HasIndex(x => x.ExpiresAt);
                e.HasOne(c => c.User)
                 .WithMany()
                 .HasForeignKey(c => c.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // AiEstimationLog — query indexes for cost analytics
            builder.Entity<AiEstimationLog>(e =>
            {
                e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
                e.HasIndex(x => x.CreatedAt);
            });

            // ────────────────────────────────────────────────────────────────

            // ── Permission System Relationships ──────────────────────────────

            builder.Entity<AppRolePermissionGroup>()
                .HasKey(rg => new { rg.RoleId, rg.PermissionGroupId });

            builder.Entity<AppRolePermissionGroup>()
                .HasOne(rg => rg.Role)
                .WithMany(r => r.PermissionGroups)
                .HasForeignKey(rg => rg.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AppRolePermissionGroup>()
                .HasOne(rg => rg.PermissionGroup)
                .WithMany(pg => pg.Roles)
                .HasForeignKey(rg => rg.PermissionGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PermissionGroupKey>()
                .HasOne(pgk => pgk.PermissionGroup)
                .WithMany(pg => pg.Permissions)
                .HasForeignKey(pgk => pgk.PermissionGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PermissionGroupKey>()
                .HasIndex(pgk => new { pgk.PermissionGroupId, pgk.Key })
                .IsUnique()
                .HasDatabaseName("UIX_PermissionGroupKey_Unique");

            // ── Multi-Tenancy Configuration ──────────────────────────────────
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (typeof(IMustHaveTenant).IsAssignableFrom(entityType.ClrType))
                {
                    var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                    var property = System.Linq.Expressions.Expression.Property(parameter, nameof(IMustHaveTenant.TenantId));
                    var dbContextMember = System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(this), nameof(CurrentTenantId));
                    var body = System.Linq.Expressions.Expression.Equal(property, dbContextMember);
                    var lambda = System.Linq.Expressions.Expression.Lambda(body, parameter);

                    builder.Entity(entityType.ClrType).HasQueryFilter(lambda);

                    builder.Entity(entityType.ClrType)
                        .Property(nameof(IMustHaveTenant.TenantId))
                        .HasDefaultValue("default-tenant-id");
                }
            }

            // Remove the default non-tenant-aware unique indexes configured by base Identity
            var userNameProp = builder.Entity<User>().Metadata.FindProperty(nameof(User.NormalizedUserName));
            if (userNameProp != null)
            {
                var index = builder.Entity<User>().Metadata.FindIndex(userNameProp);
                if (index != null) builder.Entity<User>().Metadata.RemoveIndex(index);
            }

            var emailProp = builder.Entity<User>().Metadata.FindProperty(nameof(User.NormalizedEmail));
            if (emailProp != null)
            {
                var index = builder.Entity<User>().Metadata.FindIndex(emailProp);
                if (index != null) builder.Entity<User>().Metadata.RemoveIndex(index);
            }

            var roleNameProp = builder.Entity<AppRole>().Metadata.FindProperty(nameof(AppRole.NormalizedName));
            if (roleNameProp != null)
            {
                var index = builder.Entity<AppRole>().Metadata.FindIndex(roleNameProp);
                if (index != null) builder.Entity<AppRole>().Metadata.RemoveIndex(index);
            }

            // Create new tenant-aware unique indexes
            builder.Entity<User>()
                .HasIndex(u => new { u.NormalizedUserName, u.TenantId })
                .IsUnique()
                .HasDatabaseName("UserNameIndex");

            builder.Entity<User>()
                .HasIndex(u => new { u.NormalizedEmail, u.TenantId })
                .IsUnique()
                .HasDatabaseName("EmailIndex");

            builder.Entity<AppRole>()
                .HasIndex(r => new { r.NormalizedName, r.TenantId })
                .IsUnique()
                .HasDatabaseName("RoleNameIndex");
            // ────────────────────────────────────────────────────────────────
        }

        public override int SaveChanges()
        {
            var tenantId = _tenantProvider.TenantId;
            foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (string.IsNullOrEmpty(entry.Entity.TenantId))
                    {
                        entry.Entity.TenantId = tenantId;
                    }
                }
            }
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var tenantId = _tenantProvider.TenantId;
            foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (string.IsNullOrEmpty(entry.Entity.TenantId))
                    {
                        entry.Entity.TenantId = tenantId;
                    }
                }
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
