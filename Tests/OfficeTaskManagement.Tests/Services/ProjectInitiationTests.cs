using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.Codebase;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class ProjectInitiationTests : IDisposable
    {
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;
        private readonly ApplicationDbContext _db;
        private readonly Mock<IWorkflowEngineService> _workflowEngineMock;

        public ProjectInitiationTests()
        {
            _httpHandlerMock = new Mock<HttpMessageHandler>();
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            _workflowEngineMock = new Mock<IWorkflowEngineService>();
            
            // Mock PERT calculation
            _workflowEngineMock
                .Setup(w => w.CalculatePert(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
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

        private GeminiAiService CreateService()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Gemini:ApiKey", "test-api-key" },
                    { "Gemini:GenerativeModel", "gemini-2.5-flash" }
                })
                .Build();

            var httpClient = new HttpClient(_httpHandlerMock.Object);

            var contextBuilder = new ContextBuilderService(
                _db,
                new PmKnowledgeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PmKnowledgeService>.Instance),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                NullLogger<ContextBuilderService>.Instance
            );

            var codebaseRetrieval = new CodebaseRetrievalService(
                _db,
                new Mock<IGeminiEmbeddingService>().Object,
                NullLogger<CodebaseRetrievalService>.Instance
            );

            var httpContextAccessorMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var queuedJobService = new AiQueuedJobService(_db, NullLogger<AiQueuedJobService>.Instance);

            return new GeminiAiService(
                httpClient,
                config,
                contextBuilder,
                _db,
                codebaseRetrieval,
                NullLogger<GeminiAiService>.Instance,
                httpContextAccessorMock.Object,
                queuedJobService
            );
        }

        private void SetupGeminiResponse(object payload)
        {
            var envelope = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = JsonSerializer.Serialize(payload) } }
                        }
                    }
                },
                usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 50 }
            };

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json")
                });
        }

        [Fact]
        public async Task AnalyzeProjectCodebaseAsync_ReturnsCorrectAnalysis()
        {
            // Arrange
            var project = new Project
            {
                Name = "Onboarding Test",
                RepositoryPath = ".",
                StrategicStatus = ProjectStrategicStatus.Planning
            };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var mockAnalysis = new
            {
                projectSummary = "Test Summary",
                techStack = "C# / ASP.NET",
                testOverview = "Unit tests detected.",
                testsAbsentOrIncomplete = false,
                suggestedEpics = new[]
                {
                    new { name = "Epic 1", description = "Desc 1" }
                }
            };
            SetupGeminiResponse(mockAnalysis);

            var service = CreateService();

            // Act
            var result = await service.AnalyzeProjectCodebaseAsync(project.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Summary", result.ProjectSummary);
            Assert.Equal("C# / ASP.NET", result.TechStack);
            Assert.False(result.TestsAbsentOrIncomplete);
            Assert.Single(result.SuggestedEpics);
            Assert.Equal("Epic 1", result.SuggestedEpics[0].Name);
        }

        [Fact]
        public async Task SuggestFeaturesForEpicAsync_ReturnsFeatures()
        {
            // Arrange
            var mockFeatures = new
            {
                features = new[]
                {
                    new { name = "Feature A", description = "Desc A" }
                }
            };
            SetupGeminiResponse(mockFeatures);

            var service = CreateService();

            // Act
            var result = await service.SuggestFeaturesForEpicAsync(1, "Epic 1", "Description");

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Feature A", result[0].Name);
        }

        [Fact]
        public async Task SuggestUserStoriesForFeatureAsync_ReturnsStories()
        {
            // Arrange
            var mockStories = new
            {
                stories = new[]
                {
                    new { title = "Story X", description = "Desc X", acceptanceCriteria = "Given/When/Then", priority = "High" }
                }
            };
            SetupGeminiResponse(mockStories);

            var service = CreateService();

            // Act
            var result = await service.SuggestUserStoriesForFeatureAsync(1, "Epic 1", "Feature A", "Description");

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Story X", result[0].Title);
            Assert.Equal("High", result[0].Priority);
        }

        [Fact]
        public async Task SuggestTasksAndTestCasesAsync_ReturnsTasksAndTests()
        {
            // Arrange
            var mockPayload = new
            {
                tasks = new[]
                {
                    new { title = "Task 1", description = "Desc 1", optimisticHours = 2.0, mostLikelyHours = 4.0, pessimisticHours = 8.0, priority = "Medium" }
                },
                testCases = new[]
                {
                    new { title = "TC 1", steps = "1. Open", expectedResult = "Success" }
                }
            };
            SetupGeminiResponse(mockPayload);

            var service = CreateService();

            // Act
            var result = await service.SuggestTasksAndTestCasesAsync(1, "Story 1", "Description", suggestTests: true);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tasks);
            Assert.Single(result.TestCases);
            Assert.Equal("Task 1", result.Tasks[0].Title);
            Assert.Equal(2.0m, result.Tasks[0].OptimisticHours);
            Assert.Equal("TC 1", result.TestCases[0].Title);
        }
    }
}
