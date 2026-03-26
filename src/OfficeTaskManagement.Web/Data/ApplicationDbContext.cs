using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

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

        // ── Resource Management ──────────────────────────────────────────────
        public DbSet<ResourceProfile> ResourceProfiles { get; set; }
        public DbSet<ResourceSkill> ResourceSkills { get; set; }
        public DbSet<ProjectResourceAllocation> ProjectResourceAllocations { get; set; }
        public DbSet<ResourceAvailabilityBlock> ResourceAvailabilityBlocks { get; set; }
        public DbSet<PublicHoliday> PublicHolidays { get; set; }
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
                .OnDelete(DeleteBehavior.Restrict);

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
        }
    }
}
