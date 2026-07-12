using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class SeedDataTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly ApplicationDbContext _rawDb;
        private readonly TestTenantProvider _tenantProvider;
        private readonly DbContextOptions<ApplicationDbContext> _dbOptions;

        public SeedDataTests()
        {
            _tenantProvider = new TestTenantProvider();
            _rawDb = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            var connection = _rawDb.Database.GetDbConnection();
            
            _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connection, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;

            _db = new ApplicationDbContext(_dbOptions, _tenantProvider);
        }

        public void Dispose()
        {
            var dbName = _db.Database.GetDbConnection().Database;
            _db.Dispose();
            _rawDb.Dispose();
            if (!string.IsNullOrEmpty(dbName))
            {
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task Initialize_IsIdempotent_WhenCalledMultipleTimes()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(_db);
            serviceCollection.AddSingleton<ITenantProvider>(_tenantProvider);
            serviceCollection.AddScoped<IResourceService, ResourceService>();
            serviceCollection.AddScoped<StageGateService>();
            serviceCollection.AddScoped<IWorkflowEngineService, WorkflowEngineService>();

            serviceCollection.AddIdentity<User, AppRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            serviceCollection.AddLogging();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Act - Run the first seed
            await SeedData.Initialize(serviceProvider);
            
            // Get initial counts
            var tenantCount1 = await _db.Set<Tenant>().CountAsync();
            var roleCount1 = await _db.Roles.CountAsync();
            var userCount1 = await _db.Users.CountAsync();
            var areaCount1 = await _db.Areas.CountAsync();
            var holidayCount1 = await _db.PublicHolidays.CountAsync();
            var projectCount1 = await _db.Projects.CountAsync();
            var sprintCount1 = await _db.Sprints.CountAsync();
            var epicCount1 = await _db.Epics.CountAsync();
            var featureCount1 = await _db.Features.CountAsync();
            var userStoryCount1 = await _db.UserStories.CountAsync();
            var testCaseCount1 = await _db.TestCases.CountAsync();
            var taskCount1 = await _db.Tasks.CountAsync();

            // Run the seed a second time
            await SeedData.Initialize(serviceProvider);

            // Get secondary counts
            var tenantCount2 = await _db.Set<Tenant>().CountAsync();
            var roleCount2 = await _db.Roles.CountAsync();
            var userCount2 = await _db.Users.CountAsync();
            var areaCount2 = await _db.Areas.CountAsync();
            var holidayCount2 = await _db.PublicHolidays.CountAsync();
            var projectCount2 = await _db.Projects.CountAsync();
            var sprintCount2 = await _db.Sprints.CountAsync();
            var epicCount2 = await _db.Epics.CountAsync();
            var featureCount2 = await _db.Features.CountAsync();
            var userStoryCount2 = await _db.UserStories.CountAsync();
            var testCaseCount2 = await _db.TestCases.CountAsync();
            var taskCount2 = await _db.Tasks.CountAsync();

            // Assert - Check that second run did not duplicate items
            Assert.Equal(tenantCount1, tenantCount2);
            Assert.Equal(roleCount1, roleCount2);
            Assert.Equal(userCount1, userCount2);
            Assert.Equal(areaCount1, areaCount2);
            Assert.Equal(holidayCount1, holidayCount2);
            Assert.Equal(projectCount1, projectCount2);
            Assert.Equal(sprintCount1, sprintCount2);
            Assert.Equal(epicCount1, epicCount2);
            Assert.Equal(featureCount1, featureCount2);
            Assert.Equal(userStoryCount1, userStoryCount2);
            Assert.Equal(testCaseCount1, testCaseCount2);
            Assert.Equal(taskCount1, taskCount2);
        }

        private class TestTenantProvider : ITenantProvider
        {
            private string _tenantId = "default-tenant-id";
            public string TenantId => _tenantId;

            public void SetTenant(string tenantId)
            {
                _tenantId = tenantId;
            }
        }
    }
}
