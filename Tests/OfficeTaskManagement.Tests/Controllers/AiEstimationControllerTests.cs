using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Controllers.Api;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Controllers
{
    public class AiEstimationControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly ApplicationDbContext _logDb;
        private readonly FakeTenantProvider _tenantProvider;
        private readonly Mock<IGeminiAiService> _aiMock;
        private readonly Mock<IWorkflowEngineService> _workflowMock;
        private readonly AiEstimationLogService _logService;

        public AiEstimationControllerTests()
        {
            _tenantProvider = new FakeTenantProvider();
            _tenantProvider.SetTenant("tenant-1");

            var rawDb = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            var connection = rawDb.Database.GetDbConnection();
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connection, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;
            _db = new ApplicationDbContext(dbOptions, _tenantProvider);

            var rawLogDb = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            var logConnection = rawLogDb.Database.GetDbConnection();
            var logDbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(logConnection, x => { x.MigrationsAssembly("OfficeTaskManagement"); x.UseVector(); })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;
            _logDb = new ApplicationDbContext(logDbOptions, _tenantProvider);

            _aiMock       = new Mock<IGeminiAiService>();
            _workflowMock = new Mock<IWorkflowEngineService>();

            _logService = new AiEstimationLogService(
                _logDb,
                NullLogger<AiEstimationLogService>.Instance);
        }

        public void Dispose()
        {
            var dbName1 = _db.Database.GetDbConnection().Database;
            var dbName2 = _logDb.Database.GetDbConnection().Database;

            _db.Dispose();
            _logDb.Dispose();

            if (!string.IsNullOrEmpty(dbName1))
            {
                PostgresTestDb.DropDatabaseAsync(dbName1).GetAwaiter().GetResult();
            }
            if (!string.IsNullOrEmpty(dbName2))
            {
                PostgresTestDb.DropDatabaseAsync(dbName2).GetAwaiter().GetResult();
            }
        }

        private AiEstimationController CreateController(string userId = "user-1", string tenantId = "tenant-1")
        {
            _tenantProvider.SetTenant(tenantId);
            var controller = new AiEstimationController(
                _aiMock.Object, _db, _logService, _workflowMock.Object);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("TenantId", tenantId)
            };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(claims, "Test"))
                }
            };
            return controller;
        }

        // ── EstimateAsync ────────────────────────────────────────────────────────
        [Fact]
        public async Task EstimateAsync_ValidRequest_Returns200WithResult()
        {
            var expected = new EstimationResult(
                OptimisticHours:  4,
                MostLikelyHours:  8,
                PessimisticHours: 16,
                PertHours:        9m,
                Priority:         "High",
                StoryPoints:      5,
                EstimatedBudgetBDT: 7200m,
                Confidence:       "Medium",
                Rationale:        "Complex feature",
                Risks:            Array.Empty<string>(),
                InputTokensUsed:  100,
                OutputTokensUsed: 50
            );
            _aiMock.Setup(x => x.EstimateAsync(
                    It.IsAny<EstimationRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(expected);

            var controller = CreateController();
            var request = new EstimationRequest(
                "Task", "Implement login", null, null, null, null, null);

            var result = await controller.EstimateAsync(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var value = Assert.IsType<EstimationResult>(ok.Value);
            Assert.Equal(9m, value.PertHours);
            Assert.Equal("High", value.Priority);
        }

        // ── BulkCreate — empty items returns 400 ─────────────────────────────────
        [Fact]
        public async Task BulkCreateAsync_EmptyItems_Returns400()
        {
            var controller = CreateController();
            var request = new BulkCreateRequest(Array.Empty<BulkCreateItemDto>());

            var result = await controller.BulkCreateAsync(request, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        // ── BulkCreate — Feature stored with correct TenantId ────────────────────
        [Fact]
        public async Task BulkCreateAsync_Feature_CreatesRowWithCorrectTenantId()
        {
            // Seed a minimal Epic
            _db.Epics.Add(new Epic
            {
                Id          = 1,
                Name        = "Test Epic",
                CreatedById = "user-1",
                CreatedAt   = DateTime.UtcNow,
                TenantId    = "tenant-1"
            });
            await _db.SaveChangesAsync();

            var controller = CreateController(tenantId: "tenant-1");
            var request = new BulkCreateRequest(new[]
            {
                new BulkCreateItemDto(
                    "Feature", 1, "Feature A", "Desc A", null, "High",
                    null, null, null)
            });

            var result = await controller.BulkCreateAsync(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var value = Assert.IsType<BulkCreateResult>(ok.Value);
            Assert.Single(value.CreatedIds);
            Assert.Equal("Feature", value.EntityType);

            var created = await _db.Features.FindAsync(value.CreatedIds[0]);
            Assert.NotNull(created);
            Assert.Equal("Feature A", created!.Name);
            Assert.Equal("tenant-1", created.TenantId);
        }

        // ── BulkCreate — Task PERT computed and stored ────────────────────────────
        [Fact]
        public async Task BulkCreateAsync_Task_PertIsCalculatedAndStored()
        {
            // (O + 4M + P) / 6 = (4 + 32 + 16) / 6 = 52 / 6 ≈ 8.67
            _workflowMock
                .Setup(x => x.CalculatePert(4m, 8m, 16m))
                .Returns(52m / 6m);

            _db.UserStories.Add(new UserStory
            {
                Id          = 1,
                Title       = "Test Story",
                CreatedById = "user-1",
                CreatedAt   = DateTime.UtcNow,
                TenantId    = "tenant-1"
            });
            await _db.SaveChangesAsync();

            var controller = CreateController(tenantId: "tenant-1");
            var request = new BulkCreateRequest(new[]
            {
                new BulkCreateItemDto(
                    "Task", 1, "Task A", null, null, "Medium",
                    4m, 8m, 16m)
            });

            var result = await controller.BulkCreateAsync(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var value = Assert.IsType<BulkCreateResult>(ok.Value);
            Assert.Single(value.CreatedIds);

            var created = await _db.Tasks.FindAsync(value.CreatedIds[0]);
            Assert.NotNull(created);
            Assert.Equal(4m,        created!.EstimatedOptimisticHours);
            Assert.Equal(8m,        created.EstimatedMostLikelyHours);
            Assert.Equal(16m,       created.EstimatedPessimisticHours);
            Assert.Equal(52m / 6m,  created.PertEstimatedHours);
        }

        // ── SuggestChildren returns 200 ───────────────────────────────────────────
        [Fact]
        public async Task SuggestChildrenAsync_Returns200WithSuggestions()
        {
            var suggestions = new ChildItemSuggestions(
                "Epic",
                "Feature",
                new[]
                {
                    new ChildItemDto("OAuth Integration", "OAuth desc", null, null, null, "High", null),
                    new ChildItemDto("Password Reset",    "Reset desc", null, null, null, "Medium", null)
                },
                "Two clear features identified");

            _aiMock.Setup(x => x.SuggestChildrenAsync(
                    It.IsAny<ChildRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(suggestions);

            var controller = CreateController();
            var request = new ChildRequest(
                "Epic", "Feature", "Login System", null, null, null, null, null);

            var result = await controller.SuggestChildrenAsync(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var value = Assert.IsType<ChildItemSuggestions>(ok.Value);
            Assert.Equal(2, value.Items.Length);
        }

        private class FakeTenantProvider : OfficeTaskManagement.Services.ITenantProvider
        {
            private string _tenantId = "test-tenant";
            public string TenantId => _tenantId;
            public void SetTenant(string tenantId) => _tenantId = tenantId;
        }
    }
}
