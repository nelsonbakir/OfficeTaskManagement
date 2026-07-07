using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Controllers.Api;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Ai;
using Xunit;

namespace OfficeTaskManagement.Tests.Controllers
{
    public class SprintPlannerApiControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly FakeTenantProvider _tenantProvider;
        private readonly Mock<IGeminiAiService> _aiMock;
        private readonly Mock<ICapacityPlanningService> _capacityMock;
        private readonly Mock<IResourceService> _resourceMock;

        public SprintPlannerApiControllerTests()
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

            _aiMock = new Mock<IGeminiAiService>();
            _capacityMock = new Mock<ICapacityPlanningService>();
            _resourceMock = new Mock<IResourceService>();
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

        private SprintPlannerApiController CreateController(string userId = "user-1", string tenantId = "tenant-1")
        {
            _tenantProvider.SetTenant(tenantId);
            var controller = new SprintPlannerApiController(
                _db, _aiMock.Object, _capacityMock.Object, _resourceMock.Object, NullLogger<SprintPlannerApiController>.Instance);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("TenantId", tenantId)
            };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                }
            };
            return controller;
        }

        [Fact]
        public async Task GetBacklogAsync_ReturnsBacklogTasks()
        {
            // Arrange
            var controller = CreateController();
            var project = new Project { Name = "Project A", TenantId = "tenant-1" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var task = new TaskItem
            {
                Title = "Backlog Task",
                IsBacklog = true,
                TenantId = "tenant-1",
                ProjectId = project.Id,
                Status = Models.Enums.TaskStatus.New
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            // Act
            var result = await controller.GetBacklogAsync(project.Id, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var prop = okResult.Value!.GetType().GetProperty("count");
            Assert.NotNull(prop);
            var count = (int)prop.GetValue(okResult.Value!)!;
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task ConfirmAsync_CreatesSprintAndNewTasks_InSingleTransaction()
        {
            // Arrange
            var controller = CreateController();
            var project = new Project { Name = "Project B", TenantId = "tenant-1" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var req = new ConfirmSprintPlanRequest
            {
                ProjectId = project.Id,
                Sprint = new SprintPlanDto
                {
                    Name = "Sprint One",
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(14),
                    Goal = "Deliver MVP",
                    PlannedCapacityHours = 80,
                    Tasks = new List<SprintTaskConfirmDto>
                    {
                        new SprintTaskConfirmDto
                        {
                            Title = "New Feature Task",
                            Description = "AI-suggested description",
                            Priority = "High",
                            EstimatedHours = 8m,
                            IsNewTask = true
                        }
                    }
                }
            };

            // Act
            var result = await controller.ConfirmAsync(req, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ConfirmSprintPlanResponse>(okResult.Value);
            Assert.Equal("Sprint One", response.SprintName);
            Assert.Equal(1, response.TasksCreated);

            // Verify in DB
            var sprint = await _db.Sprints.FirstOrDefaultAsync(s => s.Id == response.SprintId);
            Assert.NotNull(sprint);
            Assert.Equal("Deliver MVP", sprint.AiGeneratedGoal);
            Assert.Equal(80, sprint.PlannedCapacityHours);

            var task = await _db.Tasks.FirstOrDefaultAsync(t => t.SprintId == sprint.Id);
            Assert.NotNull(task);
            Assert.Equal("New Feature Task", task.Title);
            Assert.Equal(TaskPriority.High, task.Priority);
            Assert.Equal(8m, task.EstimatedHours);
        }

        private class FakeTenantProvider : OfficeTaskManagement.Services.ITenantProvider
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

