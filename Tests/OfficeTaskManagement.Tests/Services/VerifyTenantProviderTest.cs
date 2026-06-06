using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using Xunit;
using Xunit.Abstractions;

namespace OfficeTaskManagement.Tests.Services
{
    public class VerifyTenantProviderTest
    {
        private readonly ITestOutputHelper _output;

        public VerifyTenantProviderTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TestResolveTenantId_CircularDependencyOrException()
        {
            var services = new ServiceCollection();

            var connStr = "Host=localhost:5432;Database=OfficeTaskManagementDb;Username=school_user;Password=123456_Az;Trust Server Certificate=true";
            
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connStr));

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<ITenantProvider, TenantProvider>();

            var serviceProvider = services.BuildServiceProvider();

            // Set up HttpContext
            var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider
            };

            var tenantProvider = serviceProvider.GetRequiredService<ITenantProvider>();

            try
            {
                var tenantId = tenantProvider.TenantId;
                _output.WriteLine($"Resolved Tenant ID: '{tenantId}'");

                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                _output.WriteLine("Listing all databases in PostgreSQL server:");
                var conn = dbContext.Database.GetDbConnection();
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT datname FROM pg_database WHERE datistemplate = false;";
                
                var dbNames = new System.Collections.Generic.List<string>();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        dbNames.Add(reader.GetString(0));
                    }
                }
                _output.WriteLine($"Databases: [{string.Join(", ", dbNames)}]");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception: {ex.Message}");
                _output.WriteLine(ex.StackTrace);
            }
        }
    }
}
