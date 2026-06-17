using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Tests
{
    public static class PostgresTestDb
    {
        private static readonly PostgreSqlContainer _container;
        private static readonly string _masterConnectionString;

        static PostgresTestDb()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            _container = new PostgreSqlBuilder()
                .WithImage("pgvector/pgvector:pg16")
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCommand("-c", "max_connections=500")
                .Build();

            _container.StartAsync().GetAwaiter().GetResult();
            _masterConnectionString = _container.GetConnectionString();

            // Initialize the template database template_db
            using (var connection = new NpgsqlConnection(_masterConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE DATABASE template_db;";
                    command.ExecuteNonQuery();
                }
            }

            // Apply migrations to template_db
            var templateBuilder = new NpgsqlConnectionStringBuilder(_masterConnectionString)
            {
                Database = "template_db"
            };
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(templateBuilder.ConnectionString, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;

            using (var context = new ApplicationDbContext(options))
            {
                context.Database.Migrate();

                // Create implicit cast from text to vector so that EF Core's JSON string converter works with PostgreSQL pgvector column
                try
                {
                    context.Database.ExecuteSqlRaw("CREATE CAST (text AS vector) WITH INOUT AS IMPLICIT;");
                }
                catch
                {
                    // If the cast already exists or we don't have permission, ignore
                }

                // Seed default project (ID = 0) for tests that use default/unset ProjectId (0)
                context.Database.ExecuteSqlRaw("INSERT INTO \"Projects\" (\"Id\", \"Name\", \"TenantId\", \"CreatedAt\", \"StrategicStatus\", \"IsOnExecutiveRadar\", \"BudgetMode\") VALUES (0, 'Default Project', 'default-tenant-id', CURRENT_TIMESTAMP, 0, false, 0) ON CONFLICT DO NOTHING;");

                // Seed default workflow template (ID = 0) for tests that use default/unset WorkflowTemplateId (0)
                context.Database.ExecuteSqlRaw("INSERT INTO \"WorkflowTemplates\" (\"Id\", \"Name\", \"TenantId\", \"IsActive\") VALUES (0, 'Default Template', 'default-tenant-id', true) ON CONFLICT DO NOTHING;");

                // Seed default Epic (ID = 0) for tests that use default/unset EpicId (0)
                context.Database.ExecuteSqlRaw("INSERT INTO \"Epics\" (\"Id\", \"Name\", \"TenantId\", \"ProjectId\", \"CreatedAt\") VALUES (0, 'Default Epic', 'default-tenant-id', 0, CURRENT_TIMESTAMP) ON CONFLICT DO NOTHING;");

                // Seed default Feature (ID = 0) for tests that use default/unset FeatureId (0)
                context.Database.ExecuteSqlRaw("INSERT INTO \"Features\" (\"Id\", \"Name\", \"TenantId\", \"EpicId\", \"CreatedAt\") VALUES (0, 'Default Feature', 'default-tenant-id', 0, CURRENT_TIMESTAMP) ON CONFLICT DO NOTHING;");

                // Seed standard test users to satisfy foreign key constraints across tests
                var users = new[] { "user-1", "pm-user", "dev-user", "user-r", "user-a", "user-x", "admin_user", "user1", "user2", "user3", "user4", "peak-user" };
                foreach (var userId in users)
                {
                    if (!context.Users.Any(u => u.Id == userId))
                    {
                        context.Users.Add(new User
                        {
                            Id = userId,
                            UserName = $"{userId}@test.com",
                            Email = $"{userId}@test.com",
                            TenantId = "default-tenant-id"
                        });
                    }
                }
                context.SaveChanges();
            }

            // Clear connection pools to ensure template_db has no active connections
            NpgsqlConnection.ClearAllPools();
        }

        public static async Task<ApplicationDbContext> CreateContextAsync()
        {
            var dbName = $"db_{Guid.NewGuid():N}";

            // Clear Npgsql pools to release idle connections
            NpgsqlConnection.ClearAllPools();

            // Connect to master 'postgres' db to create the database from template
            using (var connection = new NpgsqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();

                // Terminate any active connections to template_db to ensure clone succeeds
                using (var termCmd = connection.CreateCommand())
                {
                    termCmd.CommandText = @"
                        SELECT pg_terminate_backend(pg_stat_activity.pid)
                        FROM pg_stat_activity
                        WHERE pg_stat_activity.datname = 'template_db'
                          AND pid <> pg_backend_pid();";
                    await termCmd.ExecuteNonQueryAsync();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"CREATE DATABASE \"{dbName}\" TEMPLATE template_db;";
                    await command.ExecuteNonQueryAsync();
                }
            }

            var builder = new NpgsqlConnectionStringBuilder(_masterConnectionString)
            {
                Database = dbName
            };
            var connectionString = builder.ConnectionString;

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        public static async Task DropDatabaseAsync(string dbName)
        {
            // Clear pools to release connections
            NpgsqlConnection.ClearAllPools();

            using (var connection = new NpgsqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"REVOKE CONNECT ON DATABASE ""{dbName}"" FROM public;";
                    await command.ExecuteNonQueryAsync();

                    command.CommandText = $@"
                        SELECT pg_terminate_backend(pg_stat_activity.pid)
                        FROM pg_stat_activity
                        WHERE pg_stat_activity.datname = '{dbName}'
                          AND pid <> pg_backend_pid();";
                    await command.ExecuteNonQueryAsync();

                    command.CommandText = $@"DROP DATABASE IF EXISTS ""{dbName}"";";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
