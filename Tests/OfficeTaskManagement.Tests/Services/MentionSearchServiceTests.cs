using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.Agent;
using OfficeTaskManagement.Services.Authorization;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class MentionSearchServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<IPermissionService> _mockPermSvc;

        public MentionSearchServiceTests()
        {
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            _mockPermSvc = new Mock<IPermissionService>();
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
        public async Task SearchAsync_FindsMatchingEpics_WhenPermitted()
        {
            // Arrange
            var proj = new Project { Name = "Auth Project", TenantId = "test-tenant" };
            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            var epic1 = new Epic { Name = "Auth Authentication", ProjectId = proj.Id, TenantId = "test-tenant" };
            var epic2 = new Epic { Name = "Billing Module", ProjectId = proj.Id, TenantId = "test-tenant" };
            _db.Epics.AddRange(epic1, epic2);
            await _db.SaveChangesAsync();

            // Mock manager access (so permissions filters pass all query items)
            _mockPermSvc.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var service = new MentionSearchService(_db, _mockPermSvc.Object);
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-id") }));

            // Act
            var results = await service.SearchAsync("auth", new[] { "epic" }, null, user, "test-tenant", CancellationToken.None);

            // Assert
            Assert.Single(results);
            Assert.Equal("Epic", results[0].Type);
            Assert.Equal("Auth Authentication", results[0].Label);
            Assert.Equal(epic1.Id.ToString(), results[0].Id);
        }

        [Fact]
        public async Task SearchAsync_RespectsTypeFilter()
        {
            // Arrange
            var proj = new Project { Name = "Authentication Project", TenantId = "test-tenant" };
            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            var epic = new Epic { Name = "Authentication Epic", ProjectId = proj.Id, TenantId = "test-tenant" };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            _mockPermSvc.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var service = new MentionSearchService(_db, _mockPermSvc.Object);
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-id") }));

            // Act - Only epic search
            var results = await service.SearchAsync("authentication", new[] { "epic" }, null, user, "test-tenant", CancellationToken.None);

            // Assert
            Assert.Single(results);
            Assert.Equal("Epic", results[0].Type);
            Assert.Equal("Authentication Epic", results[0].Label);
        }

        [Fact]
        public async Task SearchAsync_RespectsProjectScope()
        {
            // Arrange
            var proj1 = new Project { Id = 10, Name = "Proj 1", TenantId = "test-tenant" };
            var proj2 = new Project { Id = 11, Name = "Proj 2", TenantId = "test-tenant" };
            _db.Projects.AddRange(proj1, proj2);

            var epic1 = new Epic { Name = "Auth Epic 1", ProjectId = 10, TenantId = "test-tenant" };
            var epic2 = new Epic { Name = "Auth Epic 2", ProjectId = 11, TenantId = "test-tenant" };
            _db.Epics.AddRange(epic1, epic2);
            await _db.SaveChangesAsync();

            _mockPermSvc.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var service = new MentionSearchService(_db, _mockPermSvc.Object);
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-id") }));

            // Act - Scoped to Project 10 (epic1 should match, epic2 should not)
            var results = await service.SearchAsync("auth", new[] { "epic" }, 10, user, "test-tenant", CancellationToken.None);

            // Assert
            Assert.Single(results);
            Assert.Equal("Auth Epic 1", results[0].Label);
        }
    }
}
