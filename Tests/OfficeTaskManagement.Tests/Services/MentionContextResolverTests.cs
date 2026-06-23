using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Agent;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class MentionContextResolverTests : IDisposable
    {
        private readonly ApplicationDbContext _db;

        public MentionContextResolverTests()
        {
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            var dbName = _db.Database.GetDbConnection().Database;
            _db.Dispose();
            if (!string.IsNullOrEmpty(dbName))
            {
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task ResolveAsync_ProjectMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var proj = new Project { Name = "TaskFlow", Description = "Flow system" };
            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("Project", proj.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @Project:TaskFlow", blocks[0]);
            Assert.Contains("Description: Flow system", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_EpicMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var proj = new Project { Name = "ProjectX" };
            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            var epic = new Epic { Name = "Authentication", Description = "Auth modules", ProjectId = proj.Id };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("Epic", epic.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @Epic:Authentication", blocks[0]);
            Assert.Contains("Description: Auth modules", blocks[0]);
            Assert.Contains("Project: ProjectX", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_FeatureMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var epic = new Epic { Name = "EpicY" };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            var feat = new Feature { Name = "OAuth", Description = "Social login", EpicId = epic.Id };
            _db.Features.Add(feat);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("Feature", feat.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @Feature:OAuth", blocks[0]);
            Assert.Contains("Description: Social login", blocks[0]);
            Assert.Contains("Epic: EpicY", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_UserStoryMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var feat = new Feature { Name = "FeatureZ" };
            _db.Features.Add(feat);
            await _db.SaveChangesAsync();

            var story = new UserStory 
            { 
                Title = "As a user login", 
                Description = "I want to log in", 
                AcceptanceCriteria = "AC1 AC2",
                FeatureId = feat.Id,
                Priority = Models.Enums.TaskPriority.High
            };
            _db.UserStories.Add(story);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("UserStory", story.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @UserStory:As a user login", blocks[0]);
            Assert.Contains("Acceptance Criteria: AC1 AC2", blocks[0]);
            Assert.Contains("Priority: High", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_TaskMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var task = new TaskItem 
            { 
                Title = "Implement login controller", 
                Description = "Use OAuth endpoint",
                EstimatedOptimisticHours = 2,
                EstimatedMostLikelyHours = 4,
                EstimatedPessimisticHours = 8,
                PertEstimatedHours = 4.3m
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("Task", task.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @Task:Implement login controller", blocks[0]);
            Assert.Contains("Calculated=4.3h", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_SprintMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var proj = new Project { Name = "Sprint Project", TenantId = "test-tenant" };
            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            var sprint = new Sprint 
            { 
                Name = "Sprint 1", 
                ProjectId = proj.Id,
                StartDate = DateTime.UtcNow, 
                EndDate = DateTime.UtcNow.AddDays(14), 
                IsActive = true,
                PlannedCapacityHours = 40,
                TenantId = "test-tenant"
            };
            _db.Sprints.Add(sprint);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("Sprint", sprint.Id.ToString()) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @Sprint:Sprint 1", blocks[0]);
            Assert.Contains("Planned Capacity: 40h", blocks[0]);
        }

        [Fact]
        public async Task ResolveAsync_UserMention_ReturnsCorrectMarkdown()
        {
            // Arrange
            var user = new User 
            { 
                UserName = "john.doe", 
                Email = "john@example.com", 
                FullName = "John Doe",
                JobTitle = "Senior Developer",
                Department = "Engineering"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var profile = new ResourceProfile { UserId = user.Id, Department = "Engineering" };
            _db.ResourceProfiles.Add(profile);
            await _db.SaveChangesAsync();

            var resolver = new MentionContextResolver(_db);
            var refs = new[] { new MentionReference("User", user.Id) };

            // Act
            var blocks = await resolver.ResolveAsync(refs, CancellationToken.None);

            // Assert
            Assert.Single(blocks);
            Assert.Contains("### @User:John Doe", blocks[0]);
            Assert.Contains("Email: john@example.com", blocks[0]);
            Assert.Contains("Department: Engineering", blocks[0]);
        }
    }
}
