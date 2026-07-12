using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class TenantRegistrationTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly ApplicationDbContext _rawDb;
        private readonly TestTenantProvider _tenantProvider;
        private readonly DbContextOptions<ApplicationDbContext> _dbOptions;

        public TenantRegistrationTests()
        {
            _tenantProvider = new TestTenantProvider();
            
            // Build temporary DB to copy connection options
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
        public async Task RegisterNewTenant_SeedsRolesAndReferenceDataCorrectly()
        {
            // Arrange
            var newTenantId = Guid.NewGuid().ToString();
            var newTenant = new Tenant
            {
                Id = newTenantId,
                Name = "Test Space Org",
                Identifier = "testspace"
            };

            _db.Set<Tenant>().Add(newTenant);
            await _db.SaveChangesAsync();

            // Set up Service Provider Mocking for the seed method
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(_db);
            serviceCollection.AddSingleton<ITenantProvider>(_tenantProvider);
            
            // Register Identity Stores and Managers
            serviceCollection.AddIdentity<User, AppRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
                
            serviceCollection.AddLogging();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Act: Seed the new tenant using our newly completed seeding method
            await SeedData.SeedNewTenantAsync(serviceProvider, newTenantId);

            // Assert: Verify roles are created in the database and linked to this new tenant
            _tenantProvider.SetTenant(newTenantId);
            
            var superAdminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Super Admin");
            var developerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Developer");
            
            Assert.NotNull(superAdminRole);
            Assert.Equal(newTenantId, superAdminRole.TenantId);
            
            Assert.NotNull(developerRole);
            Assert.Equal(newTenantId, developerRole.TenantId);

            // Assert: Verify permission groups are isolated to this tenant
            var permissionGroups = await _db.PermissionGroups.ToListAsync();
            Assert.NotEmpty(permissionGroups);
            Assert.All(permissionGroups, pg => Assert.Equal(newTenantId, pg.TenantId));

            // Assert: Verify reference areas are isolated to this tenant
            var areas = await _db.Areas.ToListAsync();
            Assert.Equal(5, areas.Count);
            Assert.All(areas, a => Assert.Equal(newTenantId, a.TenantId));
        }

        private class TestTenantProvider : ITenantProvider
        {
            private string _tenantId = "test-tenant-123";
            public string TenantId => _tenantId;

            public void SetTenant(string tenantId)
            {
                _tenantId = tenantId;
            }
        }
    }
}
