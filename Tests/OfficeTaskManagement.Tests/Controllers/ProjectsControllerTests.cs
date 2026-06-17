using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Controllers;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.ViewModels;
using Xunit;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Services.Codebase;

namespace OfficeTaskManagement.Tests.Controllers
{
    public class ProjectsControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly Mock<IMediaService> _mockMediaService;
        private readonly Mock<IBudgetService> _mockBudgetService;
        private readonly ProjectsController _controller;

        public ProjectsControllerTests()
        {
            _context = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            _mockMediaService = new Mock<IMediaService>();
            _mockBudgetService = new Mock<IBudgetService>();

            _controller = new ProjectsController(_context, _mockMediaService.Object, _mockBudgetService.Object);

            var httpContext = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Role, "Manager")
            }, "TestAuthentication");
            httpContext.User = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var tempDataProvider = new Mock<ITempDataProvider>();
            _controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        }

        public void Dispose()
        {
            var dbName = _context.Database.GetDbConnection().Database;
            _context.Dispose();
            if (!string.IsNullOrEmpty(dbName))
            {
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task TestRealEmbeddingServiceAsync()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<Program>()
                .Build();

            var apiKey = config["Gemini:ApiKey"];
            var model = config["Gemini:EmbeddingModel"];
            
            Console.WriteLine($"API Key configured: {!string.IsNullOrEmpty(apiKey)}");
            Console.WriteLine($"Model: {model}");

            if (string.IsNullOrEmpty(apiKey))
            {
                // If API Key is not configured, we can't test. Skip or fail.
                return;
            }

            using var httpClient = new HttpClient();
            var embeddingService = new GeminiEmbeddingService(httpClient, config, NullLogger<GeminiEmbeddingService>.Instance);

            try
            {
                var result = await embeddingService.EmbedAsync("Hello world");
                Console.WriteLine($"Embedding succeeded. Dimensions: {result.Length}");
                Assert.NotEmpty(result);
                Assert.Equal(768, result.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Embedding failed: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task TempDiagnosticTest()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<Program>()
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost:5432;Database=OfficeTaskManagementDb;Username=school_user;Password=123456_Az;Trust Server Certificate=true";

            Console.WriteLine($"ConnStr: {connStr}");

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connStr, o => o.UseVector());

            using var db = new ApplicationDbContext(optionsBuilder.Options);

            var projects = await db.Projects.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            Console.WriteLine($"--- Projects ---");
            foreach (var p in projects)
            {
                Console.WriteLine($"Id: {p.Id}, Name: {p.Name}, RepoPath: '{p.RepositoryPath}', RepoUrl: '{p.RepositoryUrl}', Status: {p.StrategicStatus}, TenantId: '{p.TenantId}'");
            }

            var embeddingsCount = await db.CodeEmbeddings.IgnoreQueryFilters().CountAsync();
            Console.WriteLine($"Total CodeEmbeddings in DB: {embeddingsCount}");

            var projectsWithEmbeddings = await db.CodeEmbeddings.IgnoreQueryFilters()
                .GroupBy(e => e.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToListAsync();

            Console.WriteLine($"--- CodeEmbeddings by Project ---");
            foreach (var pe in projectsWithEmbeddings)
            {
                Console.WriteLine($"ProjectId: {pe.ProjectId}, Count: {pe.Count}");
            }
        }

        private class TestTenantProvider : ITenantProvider
        {
            public string TenantId { get; set; } = "default-tenant-id";
            public void SetTenant(string tenantId) => TenantId = tenantId;
        }

        [Fact]
        public async Task TestRunIndexerAsync()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<Program>()
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost:5432;Database=OfficeTaskManagementDb;Username=school_user;Password=123456_Az;Trust Server Certificate=true";

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connStr, o => o.UseVector()));
            
            services.AddScoped<ITenantProvider, TestTenantProvider>();
            services.AddLogging(builder => builder.AddConsole());
            services.AddHttpClient<IGeminiEmbeddingService, GeminiEmbeddingService>();
            
            var serviceProvider = services.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var indexer = new CodebaseIndexingService(
                scopeFactory,
                config,
                NullLogger<CodebaseIndexingService>.Instance
            );

            try
            {
                Console.WriteLine("Running IndexProjectAsync for Project 3...");
                await indexer.IndexProjectAsync(3, CancellationToken.None);
                Console.WriteLine("IndexProjectAsync completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IndexProjectAsync threw exception: {ex}");
                throw;
            }
        }

        [Fact]
        public async Task Edit_Get_ReturnsProjectViewModelWithProject_WhenProjectExists()
        {
            // Arrange
            var project = new Project
            {
                Id = 3,
                Name = "Gamma AI Recommendation Engine",
                Description = "Seeded Description",
                RequiredSkills = "AI/ML",
                TenantId = "default-tenant-id"
            };
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.Edit(3);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ProjectViewModel>(viewResult.Model);
            Assert.NotNull(model.Project);
            Assert.Equal(3, model.Project.Id);
            Assert.Equal("Gamma AI Recommendation Engine", model.Project.Name);
        }

        [Fact]
        public async Task Edit_Post_UpdatesProject_WhenModelIsValid()
        {
            // Arrange
            var project = new Project
            {
                Id = 3,
                Name = "Gamma AI Recommendation Engine",
                Description = "Seeded Description",
                RequiredSkills = "AI/ML",
                TenantId = "default-tenant-id"
            };
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            var vm = new ProjectViewModel
            {
                Project = new Project
                {
                    Id = 3,
                    Name = "Updated Project Name",
                    Description = "Updated Description",
                    RequiredSkills = "AI/ML, Python",
                    RepositoryPath = "C:\\Projects\\Updated",
                    RepositoryUrl = "https://github.com/test/updated.git"
                }
            };

            // Act
            var result = await _controller.Edit(3, vm);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var updatedProject = await _context.Projects.FindAsync(3);
            Assert.NotNull(updatedProject);
            Assert.Equal("Updated Project Name", updatedProject.Name);
            Assert.Equal("Updated Description", updatedProject.Description);
            Assert.Equal("AI/ML, Python", updatedProject.RequiredSkills);
            Assert.Equal("C:\\Projects\\Updated", updatedProject.RepositoryPath);
            Assert.Equal("https://github.com/test/updated.git", updatedProject.RepositoryUrl);
        }
    }
}
