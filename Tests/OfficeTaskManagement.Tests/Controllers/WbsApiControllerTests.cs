using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using OfficeTaskManagement.Controllers.Api;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Controllers
{
    public class WbsApiControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly FakeTenantProvider _tenantProvider;
        private readonly Mock<IWorkflowEngineService> _workflowEngineMock;

        public WbsApiControllerTests()
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

            _workflowEngineMock = new Mock<IWorkflowEngineService>();
            _workflowEngineMock.Setup(w => w.CalculatePert(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
                .Returns((decimal o, decimal m, decimal p) => (o + 4 * m + p) / 6);
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

        private WbsApiController CreateController(string userId = "user-1", string tenantId = "tenant-1")
        {
            _tenantProvider.SetTenant(tenantId);
            var controller = new WbsApiController(_db, _workflowEngineMock.Object);

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
        public async Task BulkCreateWbsAsync_NewEpicAndFeature_CreatesThem()
        {
            // Arrange
            var controller = CreateController();
            var project = new Project { Name = "Greenfield Proj", TenantId = "tenant-1" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var wbsJson = @"{
                ""projectId"": " + project.Id + @",
                ""wbs"": [
                    {
                        ""name"": ""Epic A"",
                        ""description"": ""Desc A"",
                        ""features"": [
                            {
                                ""name"": ""Feature A.1"",
                                ""description"": ""Desc A.1""
                            }
                        ]
                    }
                ]
            }";

            using var doc = JsonDocument.Parse(wbsJson);

            // Act
            var result = await controller.BulkCreateWbsAsync(doc.RootElement, CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            var epics = await _db.Epics.ToListAsync();
            Assert.Single(epics);
            Assert.Equal("Epic A", epics[0].Name);

            var features = await _db.Features.ToListAsync();
            Assert.Single(features);
            Assert.Equal("Feature A.1", features[0].Name);
            Assert.Equal(epics[0].Id, features[0].EpicId);
        }

        [Fact]
        public async Task BulkCreateWbsAsync_ExistingEpicName_ReusesEpicAndUpdatesDescription()
        {
            // Arrange
            var controller = CreateController();
            var project = new Project { Name = "Brownfield Proj", TenantId = "tenant-1" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var existingEpic = new Epic
            {
                ProjectId = project.Id,
                Name = "Epic A",
                Description = "Original Desc",
                TenantId = "tenant-1"
            };
            _db.Epics.Add(existingEpic);
            await _db.SaveChangesAsync();

            var wbsJson = @"{
                ""projectId"": " + project.Id + @",
                ""wbs"": [
                    {
                        ""name"": ""Epic A"",
                        ""description"": ""Updated Desc"",
                        ""features"": [
                            {
                                ""name"": ""Feature A.1"",
                                ""description"": ""Desc A.1""
                            }
                        ]
                    }
                ]
            }";

            using var doc = JsonDocument.Parse(wbsJson);

            // Act
            var result = await controller.BulkCreateWbsAsync(doc.RootElement, CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            var epics = await _db.Epics.ToListAsync();
            Assert.Single(epics);
            Assert.Equal("Epic A", epics[0].Name);
            Assert.Equal("Updated Desc", epics[0].Description);

            var features = await _db.Features.ToListAsync();
            Assert.Single(features);
            Assert.Equal("Feature A.1", features[0].Name);
            Assert.Equal(existingEpic.Id, features[0].EpicId);
        }

        [Fact]
        public async Task BulkCreateWbsAsync_ExistingEpicId_ReusesAndRenamesEpic()
        {
            // Arrange
            var controller = CreateController();
            var project = new Project { Name = "Rename Proj", TenantId = "tenant-1" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var existingEpic = new Epic
            {
                ProjectId = project.Id,
                Name = "Old Epic Name",
                Description = "Desc",
                TenantId = "tenant-1"
            };
            _db.Epics.Add(existingEpic);
            await _db.SaveChangesAsync();

            var wbsJson = @"{
                ""projectId"": " + project.Id + @",
                ""wbs"": [
                    {
                        ""id"": " + existingEpic.Id + @",
                        ""name"": ""New Epic Name"",
                        ""description"": ""Desc"",
                        ""features"": []
                    }
                ]
            }";

            using var doc = JsonDocument.Parse(wbsJson);

            // Act
            var result = await controller.BulkCreateWbsAsync(doc.RootElement, CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            var epics = await _db.Epics.ToListAsync();
            Assert.Single(epics);
            Assert.Equal("New Epic Name", epics[0].Name);
            Assert.Equal(existingEpic.Id, epics[0].Id);
        }

        [Fact]
        public async Task BulkCreateWbsAsync_CrossProjectEpic_CreatesUnderCorrectProject()
        {
            // Arrange
            var controller = CreateController();
            var project1 = new Project { Name = "Proj 1", TenantId = "tenant-1" };
            var project2 = new Project { Name = "Proj 2", TenantId = "tenant-1" };
            _db.Projects.AddRange(project1, project2);
            await _db.SaveChangesAsync();

            var wbsJson = @"{
                ""projectId"": " + project1.Id + @",
                ""wbs"": [
                    {
                        ""projectId"": " + project2.Id + @",
                        ""name"": ""Epic in Proj 2"",
                        ""description"": ""Desc"",
                        ""features"": []
                    }
                ]
            }";

            using var doc = JsonDocument.Parse(wbsJson);

            // Act
            var result = await controller.BulkCreateWbsAsync(doc.RootElement, CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            var epics = await _db.Epics.ToListAsync();
            Assert.Single(epics);
            Assert.Equal("Epic in Proj 2", epics[0].Name);
            Assert.Equal(project2.Id, epics[0].ProjectId);
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
