using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class MultiTenancyTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly FakeTenantProvider _tenantProvider;

        public MultiTenancyTests()
        {
            _tenantProvider = new FakeTenantProvider();
            _context = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            
            var connection = _context.Database.GetDbConnection();
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connection, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;
            _context = new ApplicationDbContext(dbOptions, _tenantProvider);
        }

        public void Dispose()
        {
            var dbName = _context.Database.GetDbConnection().Database;
            _context.Dispose();
            if (!string.IsNullOrEmpty(dbName))
            {
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task SaveChanges_AutoPopulatesTenantId()
        {
            // Arrange
            _tenantProvider.SetTenant("tenant-123");
            var project = new Project { Name = "Test Project" };

            // Act
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            // Assert
            Assert.Equal("tenant-123", project.TenantId);
        }

        [Fact]
        public async Task GlobalQueryFilter_IsolatesDataBetweenTenants()
        {
            // Arrange
            // Seed Tenant 1 data
            _tenantProvider.SetTenant("tenant-1");
            _context.Projects.Add(new Project { Name = "Project 1" });
            _context.Projects.Add(new Project { Name = "Project 2" });
            await _context.SaveChangesAsync();

            // Seed Tenant 2 data
            _tenantProvider.SetTenant("tenant-2");
            _context.Projects.Add(new Project { Name = "Project 3" });
            await _context.SaveChangesAsync();

            // Act & Assert 1: Query as Tenant 1
            _tenantProvider.SetTenant("tenant-1");
            var tenant1Projects = await _context.Projects.ToListAsync();
            Assert.Equal(2, tenant1Projects.Count);
            Assert.All(tenant1Projects, p => Assert.Equal("tenant-1", p.TenantId));

            // Act & Assert 2: Query as Tenant 2
            _tenantProvider.SetTenant("tenant-2");
            var tenant2Projects = await _context.Projects.ToListAsync();
            Assert.Single(tenant2Projects);
            Assert.Equal("tenant-2", tenant2Projects[0].TenantId);
        }

        [Fact]
        public async Task IgnoreQueryFilters_BypassesIsolation()
        {
            // Arrange
            _tenantProvider.SetTenant("tenant-1");
            _context.Projects.Add(new Project { Name = "Project 1" });
            
            _tenantProvider.SetTenant("tenant-2");
            _context.Projects.Add(new Project { Name = "Project 2" });
            await _context.SaveChangesAsync();

            // Act
            var allProjects = await _context.Projects.IgnoreQueryFilters().ToListAsync();

            // Assert
            Assert.Contains(allProjects, p => p.Name == "Project 1");
            Assert.Contains(allProjects, p => p.Name == "Project 2");
            Assert.True(allProjects.Count >= 2);
        }

        private class FakeTenantProvider : ITenantProvider
        {
            private string _tenantId = string.Empty;
            public string TenantId => _tenantId;

            public void SetTenant(string tenantId)
            {
                _tenantId = tenantId;
            }
        }
    }
}
