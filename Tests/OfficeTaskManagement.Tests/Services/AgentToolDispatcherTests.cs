using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.Agent;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    /// <summary>
    /// Tests AgentToolDispatcher — the function-call router for the AI Copilot.
    /// Uses PostgresTestDb (Testcontainers + pgvector/pg16) matching the full test suite.
    ///
    /// IMPORTANT: Always use _db.CurrentTenantId (= "test-tenant") for tenantId on all
    /// entities and dispatcher calls. The ApplicationDbContext global query filter
    /// filters every IMustHaveTenant entity by CurrentTenantId. If you set an explicit
    /// TenantId that doesn't match (e.g. "default-tenant-id"), SaveChangesAsync won't
    /// override it (only overrides when empty) and queries will silently return nothing.
    /// </summary>
    public class AgentToolDispatcherTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly string _tenantId;          // = "test-tenant" from TestTenantProvider
        private readonly Mock<IWorkflowEngineService> _workflowMock;
        private readonly AgentToolDispatcher _dispatcher;

        public AgentToolDispatcherTests()
        {
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            _tenantId = _db.CurrentTenantId; // "test-tenant"

            _workflowMock = new Mock<IWorkflowEngineService>();
            _workflowMock
                .Setup(x => x.CalculatePert(
                    It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
                .Returns<decimal, decimal, decimal>((o, m, p) => (o + 4 * m + p) / 6);

            var pmReportLogger = NullLogger<OfficeTaskManagement.Services.Ai.PmReportService>.Instance;
            var pmReport = new OfficeTaskManagement.Services.Ai.PmReportService(_db, pmReportLogger);

            _dispatcher = new AgentToolDispatcher(
                _db, _workflowMock.Object, pmReport,
                NullLogger<AgentToolDispatcher>.Instance);
        }

        public void Dispose()
        {
            var dbName = _db.Database.GetDbConnection().Database;
            _db.Dispose();
            if (!string.IsNullOrEmpty(dbName))
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
        }

        // ── Helper: build JSON args safely via JsonSerializer ─────────────────
        private static JsonElement Json(object obj)
            => JsonDocument.Parse(JsonSerializer.Serialize(obj)).RootElement;

        // ── create_feature inserts a Feature row ──────────────────────────────
        [Fact]
        public async Task DispatchAsync_CreateFeature_InsertsFeatureRow()
        {
            // Seed a parent Epic with no explicit TenantId — SaveChangesAsync
            // will set it to CurrentTenantId ("test-tenant") automatically.
            var epic = new Epic
            {
                Name        = "Auth Epic",
                ProjectId   = 0, // seeded default project
                CreatedById = "user-1",
                CreatedAt   = DateTime.UtcNow
                // TenantId intentionally omitted — let SaveChanges set it
            };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            var args = Json(new { epicId = epic.Id, name = "Login UI", description = "Login screen" });

            // Pass _tenantId so the dispatcher stores the feature under the same tenant
            var result = await _dispatcher.DispatchAsync(
                "create_feature", args, "user-1", _tenantId, CancellationToken.None);

            Assert.Contains("Feature created", result);

            // The global query filter uses CurrentTenantId = "test-tenant" — should match
            var featureCount = await _db.Features
                .AsNoTracking()
                .CountAsync(f => f.Name == "Login UI" && f.EpicId == epic.Id);
            Assert.True(featureCount > 0, $"Expected 'Login UI' feature in DB (tenant={_tenantId}) but found none.");
        }

        // ── create_task calculates PERT and stores it ─────────────────────────
        [Fact]
        public async Task DispatchAsync_CreateTask_CalculatesAndStoresPert()
        {
            // Seed a Feature then a UserStory under it (no explicit TenantId)
            var epic = new Epic
            {
                Name = "Task Test Epic", ProjectId = 0, CreatedById = "user-1",
                CreatedAt = DateTime.UtcNow
            };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            var feature = new Feature
            {
                Name = "Task Test Feature", EpicId = epic.Id, CreatedById = "user-1",
                CreatedAt = DateTime.UtcNow
            };
            _db.Features.Add(feature);
            await _db.SaveChangesAsync();

            var story = new UserStory
            {
                Title = "JWT Story", FeatureId = feature.Id,
                CreatedById = "user-1", CreatedAt = DateTime.UtcNow
            };
            _db.UserStories.Add(story);
            await _db.SaveChangesAsync();

            // PERT = (4 + 4×8 + 16) / 6 = 52/6
            var args = Json(new
            {
                userStoryId      = story.Id,
                title            = "Implement JWT",
                optimisticHours  = 4.0,
                mostLikelyHours  = 8.0,
                pessimisticHours = 16.0
            });

            var result = await _dispatcher.DispatchAsync(
                "create_task", args, "user-1", _tenantId, CancellationToken.None);

            Assert.Contains("Task created", result);

            var task = await _db.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Title == "Implement JWT" && t.UserStoryId == story.Id);
            Assert.NotNull(task);
            Assert.Equal(52m / 6m, task!.PertEstimatedHours);
        }

        // ── unknown function name returns error string (no exception) ─────────
        [Fact]
        public async Task DispatchAsync_UnknownFunction_ReturnsErrorString()
        {
            var args   = Json(new { });
            var result = await _dispatcher.DispatchAsync(
                "do_something_weird", args, "user-1", _tenantId, CancellationToken.None);

            Assert.Contains("Unknown function", result);
        }

        // ── get_sprint_capacity returns sprint info ────────────────────────────
        [Fact]
        public async Task DispatchAsync_GetSprintCapacity_ReturnsSummary()
        {
            // Seed a sprint — no explicit TenantId
            var sprint = new Sprint
            {
                Name      = "Sprint Alpha",
                ProjectId = 0,   // seeded default project
                StartDate = DateTime.UtcNow.Date,
                EndDate   = DateTime.UtcNow.Date.AddDays(14)
            };
            _db.Sprints.Add(sprint);
            await _db.SaveChangesAsync();

            Assert.True(sprint.Id > 0, "Sprint was not saved — Id is still 0.");

            var args = Json(new { sprintId = sprint.Id });

            var result = await _dispatcher.DispatchAsync(
                "get_sprint_capacity", args, "user-1", _tenantId, CancellationToken.None);

            Assert.Contains("Sprint Alpha", result);
        }

        // ── update_estimate updates PERT fields ───────────────────────────────
        [Fact]
        public async Task DispatchAsync_UpdateEstimate_UpdatesTaskPert()
        {
            // Seed Epic → Feature → UserStory → Task (all without explicit TenantId)
            var epic = new Epic
            {
                Name = "Update Test Epic", ProjectId = 0, CreatedById = "user-1",
                CreatedAt = DateTime.UtcNow
            };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync();

            var feature = new Feature
            {
                Name = "Update Test Feature", EpicId = epic.Id, CreatedById = "user-1",
                CreatedAt = DateTime.UtcNow
            };
            _db.Features.Add(feature);
            await _db.SaveChangesAsync();

            var story = new UserStory
            {
                Title = "Update Test Story", FeatureId = feature.Id,
                CreatedById = "user-1", CreatedAt = DateTime.UtcNow
            };
            _db.UserStories.Add(story);
            await _db.SaveChangesAsync();

            var task = new TaskItem
            {
                Title          = "Task to update",
                UserStoryId    = story.Id,
                EstimatedHours = 8m,
                CreatedById    = "user-1",
                CreatedAt      = DateTime.UtcNow
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            Assert.True(task.Id > 0, "Task was not saved — Id is still 0.");

            // PERT = (2 + 4×5 + 10) / 6 = 32/6
            var args = Json(new
            {
                taskId           = task.Id,
                optimisticHours  = 2.0,
                mostLikelyHours  = 5.0,
                pessimisticHours = 10.0
            });

            var result = await _dispatcher.DispatchAsync(
                "update_estimate", args, "user-1", _tenantId, CancellationToken.None);

            Assert.Contains("estimate updated", result);

            _db.Entry(task).State = EntityState.Detached;
            var updated = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == task.Id);
            Assert.NotNull(updated);
            Assert.Equal(32m / 6m, updated!.PertEstimatedHours);
        }
    }
}
