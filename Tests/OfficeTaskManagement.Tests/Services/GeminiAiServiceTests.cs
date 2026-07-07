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
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.Codebase;
using Xunit;


namespace OfficeTaskManagement.Tests.Services
{
    public class GeminiAiServiceTests : IDisposable
    {
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;
        private readonly ApplicationDbContext _db;

        public GeminiAiServiceTests()
        {
            _httpHandlerMock = new Mock<HttpMessageHandler>();
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

        private GeminiAiService CreateService(string? apiKey = "test-key")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Gemini:ApiKey", apiKey },
                    { "Gemini:GenerativeModel", "gemini-2.5-flash" }
                })
                .Build();

            var httpClient = new HttpClient(_httpHandlerMock.Object);

            // ContextBuilderService with InMemory DB (returns empty context — fast for unit tests)
            var contextBuilder = new ContextBuilderService(
                _db,
                new PmKnowledgeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                    NullLogger<PmKnowledgeService>.Instance),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
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

        private void SetupGeminiResponse(object payload, HttpStatusCode status = HttpStatusCode.OK)
        {
            var geminiEnvelope = new
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
                usageMetadata = new { promptTokenCount = 500, candidatesTokenCount = 200 }
            };

            var responseJson = JsonSerializer.Serialize(geminiEnvelope);
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(status)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
        }

        [Fact]
        public async Task EstimateAsync_ValidResponse_ReturnsPertHours()
        {
            // Arrange — mock Gemini returning well-formed JSON
            SetupGeminiResponse(new
            {
                optimisticHours    = 4.0,
                mostLikelyHours    = 8.0,
                pessimisticHours   = 16.0,
                pertHours          = 9.0,
                priority           = "High",
                storyPoints        = 8,
                estimatedBudgetBDT = 7200.0,
                confidence         = "Medium",
                rationale          = "Based on 3 similar tasks.",
                risks              = new[] { "Scope creep risk" }
            });

            var service = CreateService();
            var request = new EstimationRequest("Task", "Implement JWT Auth", null, 1, null, null, null);

            // Act
            var result = await service.EstimateAsync(request);

            // Assert
            Assert.Equal(4.0m,  result.OptimisticHours);
            Assert.Equal(8.0m,  result.MostLikelyHours);
            Assert.Equal(16.0m, result.PessimisticHours);
            Assert.Equal(9.0m,  result.PertHours);
            Assert.Equal("High", result.Priority);
            Assert.Equal("Medium", result.Confidence);
            Assert.Single(result.Risks);
            Assert.Equal(500, result.InputTokensUsed);
            Assert.Equal(200, result.OutputTokensUsed);
        }

        [Fact]
        public async Task EstimateAsync_ApiKeyMissing_ReturnsFallbackEstimate()
        {
            // Arrange — service with no API key configured
            var service = CreateService(apiKey: null);

            // Act
            var result = await service.EstimateAsync(
                new EstimationRequest("Task", "Test task", null, null, null, null, null));

            // Assert — falls back to safe defaults, does not throw
            Assert.NotNull(result);
            Assert.Equal("Low", result.Confidence);
            Assert.Contains("unavailable", result.Rationale, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, result.InputTokensUsed);
        }

        [Fact]
        public async Task EstimateAsync_MalformedJson_ReturnsFallback()
        {
            // Arrange — Gemini returns a garbage text response inside valid envelope
            var malformedJsonText = "{ malformed json" + "}}}" ; // prevent C# parser confusion
            var badEnvelope = new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = malformedJsonText } } } }
                },
                usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 50 }
            };

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(badEnvelope), Encoding.UTF8, "application/json")
                });

            var service = CreateService();
            var result = await service.EstimateAsync(
                new EstimationRequest("Feature", "Login UI", null, 1, null, null, null));

            // Should not throw — returns fallback
            Assert.NotNull(result);
            Assert.Equal("Low", result.Confidence);
        }

        [Fact]
        public async Task EstimateAsync_ApiReturns429_RetriesAndSucceeds()
        {
            // Arrange: first call returns 429, second call returns valid response
            int callCount = 0;
            var validPayload = new
            {
                optimisticHours    = 4.0,
                mostLikelyHours    = 8.0,
                pessimisticHours   = 16.0,
                pertHours          = 9.0,
                priority           = "Medium",
                storyPoints        = 5,
                estimatedBudgetBDT = 6400.0,
                confidence         = "Medium",
                rationale          = "Test rationale",
                risks              = new string[0]
            };

            var validEnvelope = JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(validPayload) } } } } },
                usageMetadata = new { promptTokenCount = 400, candidatesTokenCount = 150 }
            });

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                        return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                        {
                            Content = new StringContent("{}", Encoding.UTF8, "application/json")
                        };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(validEnvelope, Encoding.UTF8, "application/json")
                    };
                });

            var service = CreateService();

            // Act — with a very short retry delay for testing
            var result = await service.EstimateAsync(
                new EstimationRequest("Task", "Test", null, 1, null, null, null));

            // Assert
            Assert.True(callCount >= 2, $"Expected at least 2 calls (429 + retry), got {callCount}");
            Assert.Equal(8.0m, result.MostLikelyHours);
        }

        [Fact]
        public async Task ProposeSprintGoalAsync_ValidResponse_ReturnsProposals()
        {
            // Arrange
            SetupGeminiResponse(new
            {
                goalStatement = "Deliver core features.",
                keyThemes = new[] { "Setup", "Authentication" },
                riskSummary = "No major risks.",
                recommendedSprintName = "Sprint 1 - Core"
            });

            var project = new OfficeTaskManagement.Models.Project
            {
                Name = "Project Alpha",
                TenantId = "test-tenant"
            };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var task = new OfficeTaskManagement.Models.TaskItem
            {
                Title = "Initial Task",
                IsBacklog = true,
                TenantId = "test-tenant",
                ProjectId = project.Id
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            var service = CreateService();

            // Act
            var result = await service.ProposeSprintGoalAsync(project.Id, DateTime.UtcNow, DateTime.UtcNow.AddDays(14));

            // Assert
            Assert.Equal("Deliver core features.", result.GoalStatement);
            Assert.Equal("Sprint 1 - Core", result.RecommendedSprintName);
            Assert.Equal(2, result.KeyThemes.Length);
        }

        [Fact]
        public async Task SelectSprintBacklogAsync_ValidResponse_ReturnsSelectedTasks()
        {
            // Arrange
            SetupGeminiResponse(new
            {
                selectedTasks = new[]
                {
                    new
                    {
                        taskId = 101,
                        title = "Selected Task 1",
                        description = "Desc",
                        priority = "High",
                        optimisticHours = 2.0,
                        mostLikelyHours = 4.0,
                        pessimisticHours = 8.0,
                        pertHours = 4.3,
                        storyPoints = 5,
                        rationale = "Fits capacity",
                        isNewTask = false
                    }
                }
            });

            var project = new OfficeTaskManagement.Models.Project
            {
                Name = "Project Beta",
                TenantId = "test-tenant"
            };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var task = new OfficeTaskManagement.Models.TaskItem
            {
                Id = 101,
                Title = "Selected Task 1",
                IsBacklog = true,
                TenantId = "test-tenant",
                ProjectId = project.Id
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            var service = CreateService();

            // Act
            var result = await service.SelectSprintBacklogAsync(project.Id, DateTime.UtcNow, DateTime.UtcNow.AddDays(14), 40m);

            // Assert
            Assert.Single(result);
            Assert.Equal("Selected Task 1", result[0].Title);
            Assert.Equal(4.3m, result[0].PertHours);
            Assert.Equal("Fits capacity", result[0].Rationale);
        }

        [Fact]
        public async Task AssignSprintTasksAsync_ValidResponse_ReturnsSuggestedAssignments()
        {
            // Arrange
            SetupGeminiResponse(new
            {
                assignments = new[]
                {
                    new
                    {
                        taskId = 201,
                        title = "Task 1",
                        assigneeUserId = "user-123",
                        assigneeName = "John Doe",
                        assignmentReason = "Has CSS skills"
                    }
                }
            });

            var service = CreateService();

            var tasks = new List<SprintTaskSuggestionDto>
            {
                new SprintTaskSuggestionDto(201, "Task 1", "Desc", "High", 5m, 3m, 5m, 8m, 5, "Priority", false, true)
            };

            var resources = new List<ResourceCapacitySlotDto>
            {
                new ResourceCapacitySlotDto("user-123", "John Doe", null, "Dev", 40m, 40m, 50m, new[] { "CSS" })
            };

            // Act
            var result = await service.AssignSprintTasksAsync(1, tasks, resources);

            // Assert
            Assert.Single(result);
            Assert.Equal("user-123", result[0].SuggestedAssigneeId);
            Assert.Equal("John Doe", result[0].SuggestedAssigneeName);
            Assert.Equal("Has CSS skills", result[0].AssignmentReason);
        }
    }
}

