using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class ContextBuilderServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;

        public ContextBuilderServiceTests()
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

        private ContextBuilderService CreateService()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var pmKnowledge = new PmKnowledgeService(_db, cache, NullLogger<PmKnowledgeService>.Instance);
            return new ContextBuilderService(_db, pmKnowledge, cache, NullLogger<ContextBuilderService>.Instance);
        }

        [Fact]
        public async Task BuildContext_SiblingList_CompressesNamesToCommaSeparated()
        {
            // Arrange — project with 4 epics
            var projectId = 1;
            _db.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
            _db.Epics.AddRange(
                new Epic { ProjectId = projectId, Name = "Login" },
                new Epic { ProjectId = projectId, Name = "Payroll" },
                new Epic { ProjectId = projectId, Name = "Leave Management" },
                new Epic { ProjectId = projectId, Name = "Reporting" }
            );
            await _db.SaveChangesAsync();

            var service = CreateService();
            var request = new EstimationRequest("Epic", "New Epic", null, projectId, null, null, null);

            // Act
            var ctx = await service.BuildContextAsync(request);

            // Assert — siblings are comma-separated names, no descriptions
            Assert.NotNull(ctx.SiblingList);
            Assert.Contains("Login", ctx.SiblingList);
            Assert.Contains("Payroll", ctx.SiblingList);
            Assert.Contains("Reporting", ctx.SiblingList);
            // Should not contain raw descriptions (our model stores separate Description field)
            Assert.DoesNotContain("Description:", ctx.SiblingList);
        }

        [Fact]
        public async Task BuildContext_TokenBudgetExhausted_CodeChunksRemainsNull()
        {
            // The token budget check currently keeps CodeChunks null (Phase 3 placeholder)
            // This test verifies the Phase 3 placeholder behavior is correct
            var service = CreateService();
            var request = new EstimationRequest("Task", "X", null, null, null, null, null);

            var ctx = await service.BuildContextAsync(request);

            // Phase 3 not implemented yet — code chunks should always be null
            Assert.True(ctx.CodeChunks == null || ctx.CodeChunks.Count == 0,
                "Code chunks should be null until Phase 3 RAG is implemented");
        }

        [Fact]
        public void EstimateTokens_Text_Returns25PercentOfCharCount()
        {
            // EstimateTokens is an internal static method on ContextBuilderService
            var method = typeof(ContextBuilderService)
                .GetMethod("EstimateTokens",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

            Assert.NotNull(method);

            // "hello world" = 11 chars / 4 ≈ 2 tokens (integer division)
            var result = (int)method!.Invoke(null, new object[] { "hello world" })!;
            Assert.Equal(2, result);
        }

        [Fact]
        public async Task BuildContext_NoProject_SiblingListIsNull()
        {
            // When no parent IDs are provided, sibling list should be null
            var service = CreateService();
            var request = new EstimationRequest("Epic", "Some Epic", null, null, null, null, null);

            var ctx = await service.BuildContextAsync(request);

            Assert.Null(ctx.SiblingList);
        }

        [Fact]
        public async Task BuildContext_HourlyRate_FallsBackTo800WhenNoProjectId()
        {
            var service = CreateService();
            var request = new EstimationRequest("Task", "Task Title", null, null, null, null, null);

            var ctx = await service.BuildContextAsync(request);

            Assert.Equal(800m, ctx.HourlyRateBDT);
        }
    }
}
