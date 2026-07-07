using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
using OfficeTaskManagement.Services.Agent;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.WorkflowEngine;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class AgentServiceTests : IDisposable
    {
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;
        private readonly ApplicationDbContext _db;
        private readonly Mock<IWorkflowEngineService> _workflowMock;
        private readonly AgentToolDispatcher _dispatcher;

        public AgentServiceTests()
        {
            _httpHandlerMock = new Mock<HttpMessageHandler>();
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();

            _workflowMock = new Mock<IWorkflowEngineService>();
            _workflowMock
                .Setup(x => x.CalculatePert(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
                .Returns<decimal, decimal, decimal>((o, m, p) => (o + 4 * m + p) / 6);

            var pmReport = new PmReportService(_db, NullLogger<PmReportService>.Instance);
            _dispatcher = new AgentToolDispatcher(_db, _workflowMock.Object, pmReport, NullLogger<AgentToolDispatcher>.Instance);
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

        private AgentService CreateService(string provider = "Ollama", string? apiKey = null, string apiType = "OpenAI")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Gemini:Provider", provider },
                    { "Gemini:ApiKey", apiKey },
                    { "Gemini:CopilotModel", "gemini-2.5-pro" },
                    { "Gemini:OllamaUrl", "http://localhost:11434" },
                    { "Gemini:OllamaModel", "gemma4:12b-it-q4_k_m" },
                    { "Gemini:OpenVINOUrl", "http://localhost:8000/v1" },
                    { "Gemini:OpenVINOModel", "gemma4:12b-it-q4_k_m" },
                    { "Gemini:OpenVINOApiType", apiType },
                    { "Gemini:OpenVINOEmbeddingModel", "nomic-embed-text" }
                })
                .Build();

            var httpClient = new HttpClient(_httpHandlerMock.Object);

            var conversationService = new AgentConversationService(_db, NullLogger<AgentConversationService>.Instance);

            var contextBuilder = new ContextBuilderService(
                _db,
                new PmKnowledgeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                    NullLogger<PmKnowledgeService>.Instance),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                NullLogger<ContextBuilderService>.Instance
            );

            var mentionResolver = new MentionContextResolver(_db);

            var queuedJobService = new AiQueuedJobService(_db, NullLogger<AiQueuedJobService>.Instance);

            return new AgentService(
                httpClient,
                config,
                conversationService,
                _dispatcher,
                contextBuilder,
                NullLogger<AgentService>.Instance,
                mentionResolver,
                queuedJobService
            );
        }

        [Fact]
        public async Task ChatAsync_WithOllamaProvider_CallsOllamaAndDoesNotFallbackToGemini_OnSuccess()
        {
            // Arrange
            var service = CreateService("Ollama");
            var request = new AgentChatRequest(
                ConversationId: Guid.NewGuid().ToString(),
                UserId: "user-1",
                TenantId: _db.CurrentTenantId,
                Message: "Hello Ollama",
                EntityType: null,
                EntityId: null,
                ProjectContextId: null,
                Mentions: null
            );

            // Mock Ollama chat response
            var ollamaResponse = new
            {
                message = new
                {
                    role = "assistant",
                    content = "Hello from Ollama!"
                }
            };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(ollamaResponse), System.Text.Encoding.UTF8, "application/json")
            };

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/api/chat")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.ChatAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Hello from Ollama!", result.Message);

            // Verify that we didn't call the Gemini API endpoint
            _httpHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("generativelanguage.googleapis.com")),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ChatAsync_WithOllamaProvider_DoesNotFallbackToGemini_OnOllamaFailure()
        {
            // Arrange
            var service = CreateService("Ollama");
            var request = new AgentChatRequest(
                ConversationId: Guid.NewGuid().ToString(),
                UserId: "user-1",
                TenantId: _db.CurrentTenantId,
                Message: "Hello Ollama Failure Test",
                EntityType: null,
                EntityId: null,
                ProjectContextId: null,
                Mentions: null
            );

            // Mock Ollama failing (returning 500 or throwing, which makes CallOllamaChatAsync return null)
            var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/api/chat")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.ChatAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Encountered an issue communicating with the AI service", result.Message);

            // Verify that we never fell back to Gemini API endpoint
            _httpHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("generativelanguage.googleapis.com")),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ChatAsync_WithOpenVINOProvider_OpenAIFormat_CallsOpenAIAndDoesNotFallbackToGemini()
        {
            // Arrange
            var service = CreateService("OpenVINO", apiType: "OpenAI");
            var request = new AgentChatRequest(
                ConversationId: Guid.NewGuid().ToString(),
                UserId: "user-1",
                TenantId: _db.CurrentTenantId,
                Message: "Hello OpenVINO OpenAI Format",
                EntityType: null,
                EntityId: null,
                ProjectContextId: null,
                Mentions: null
            );

            // Mock OpenAI chat completions response
            var openAIResponse = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Hello from OpenVINO OpenAI Format!"
                        }
                    }
                }
            };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(openAIResponse), System.Text.Encoding.UTF8, "application/json")
            };

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/chat/completions")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.ChatAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Hello from OpenVINO OpenAI Format!", result.Message);

            // Verify that we didn't call the Gemini API endpoint
            _httpHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("generativelanguage.googleapis.com")),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ChatAsync_WithOpenVINOProvider_OllamaFormat_CallsOllamaAndDoesNotFallbackToGemini()
        {
            // Arrange
            var service = CreateService("OpenVINO", apiType: "Ollama");
            var request = new AgentChatRequest(
                ConversationId: Guid.NewGuid().ToString(),
                UserId: "user-1",
                TenantId: _db.CurrentTenantId,
                Message: "Hello OpenVINO Ollama Format",
                EntityType: null,
                EntityId: null,
                ProjectContextId: null,
                Mentions: null
            );

            // Mock Ollama chat response
            var ollamaResponse = new
            {
                message = new
                {
                    role = "assistant",
                    content = "Hello from OpenVINO Ollama Format!"
                }
            };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(ollamaResponse), System.Text.Encoding.UTF8, "application/json")
            };

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/api/chat")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.ChatAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Hello from OpenVINO Ollama Format!", result.Message);

            // Verify that we didn't call the Gemini API endpoint
            _httpHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("generativelanguage.googleapis.com")),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ChatAsync_WithProjectContextId_SavesConversationAsProjectScoped()
        {
            // Arrange
            var service = CreateService("Ollama");
            var convId = Guid.NewGuid().ToString();
            var request = new AgentChatRequest(
                ConversationId: convId,
                UserId: "user-1",
                TenantId: _db.CurrentTenantId,
                Message: "Hello",
                EntityType: "Epic",
                EntityId: 5,
                ProjectContextId: 10,
                Mentions: null
            );

            var ollamaResponse = new
            {
                message = new
                {
                    role = "assistant",
                    content = "Hello from Ollama!"
                }
            };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(ollamaResponse), System.Text.Encoding.UTF8, "application/json")
            };

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/api/chat")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.ChatAsync(request);

            // Assert
            Assert.NotNull(result);

            // Verify that the conversation record in the DB was created/updated as EntityType = "Project" and EntityId = 10
            var conv = await _db.AgentConversations.FirstOrDefaultAsync(c => c.Id == convId);
            Assert.NotNull(conv);
            Assert.Equal("Project", conv.EntityType);
            Assert.Equal(10, conv.EntityId);
        }
    }
}
