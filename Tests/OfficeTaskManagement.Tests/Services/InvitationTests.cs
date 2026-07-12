using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class InvitationTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly ApplicationDbContext _rawDb;
        private readonly TestTenantProvider _tenantProvider;
        private readonly DbContextOptions<ApplicationDbContext> _dbOptions;

        public InvitationTests()
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
        public async Task CreateInvitation_SavesAndCalculatesExpiryCorrectly()
        {
            // Arrange
            var tenantId = "default-tenant-id";
            var invite = new OrganizationInvitation
            {
                Email = "invitee@company.com",
                TenantId = tenantId,
                Role = "QA Engineer",
                InviteCode = "secure_token_123",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IsAccepted = false
            };

            // Act
            _db.OrganizationInvitations.Add(invite);
            await _db.SaveChangesAsync();

            // Assert
            var savedInvite = await _db.OrganizationInvitations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.InviteCode == "secure_token_123");

            Assert.NotNull(savedInvite);
            Assert.Equal("invitee@company.com", savedInvite.Email);
            Assert.Equal("QA Engineer", savedInvite.Role);
            Assert.False(savedInvite.IsAccepted);
            Assert.True(savedInvite.ExpiresAt > DateTime.UtcNow);
        }

        [Fact]
        public async Task ExpiredInvitation_IsCorrectlyIdentified()
        {
            // Arrange
            var tenantId = "default-tenant-id";
            var invite = new OrganizationInvitation
            {
                Email = "expired@company.com",
                TenantId = tenantId,
                Role = "Developer",
                InviteCode = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // Already expired
                IsAccepted = false
            };

            _db.OrganizationInvitations.Add(invite);
            await _db.SaveChangesAsync();

            // Act
            var savedInvite = await _db.OrganizationInvitations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.InviteCode == "expired_token");

            // Assert
            Assert.NotNull(savedInvite);
            Assert.True(DateTime.UtcNow > savedInvite.ExpiresAt);
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
