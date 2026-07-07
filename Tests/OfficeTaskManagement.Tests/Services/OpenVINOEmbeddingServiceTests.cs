using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using OfficeTaskManagement.Services.Ai;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class OpenVINOEmbeddingServiceTests
    {
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;

        public OpenVINOEmbeddingServiceTests()
        {
            _httpHandlerMock = new Mock<HttpMessageHandler>();
        }

        private OpenVINOEmbeddingService CreateService(string apiType = "OpenAI")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Gemini:OpenVINOUrl", "http://localhost:8000/v1" },
                    { "Gemini:OpenVINOModel", "gemma4:12b-it-q4_k_m" },
                    { "Gemini:OpenVINOApiType", apiType },
                    { "Gemini:OpenVINOEmbeddingModel", "nomic-embed-text" }
                })
                .Build();

            var httpClient = new HttpClient(_httpHandlerMock.Object);

            return new OpenVINOEmbeddingService(
                httpClient,
                config,
                NullLogger<OpenVINOEmbeddingService>.Instance
            );
        }

        [Fact]
        public async Task EmbedAsync_OpenAIFormat_ReturnsEmbeddingSuccessfully()
        {
            // Arrange
            var service = CreateService("OpenAI");
            var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
            var openAIResponse = new
            {
                data = new[]
                {
                    new
                    {
                        embedding = expectedEmbedding
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
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/embeddings")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.EmbedAsync("test text");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedEmbedding, result);
        }

        [Fact]
        public async Task EmbedAsync_OllamaFormat_ReturnsEmbeddingSuccessfully()
        {
            // Arrange
            var service = CreateService("Ollama");
            var expectedEmbedding = new float[] { 0.4f, 0.5f, 0.6f };
            var ollamaResponse = new
            {
                embeddings = new[]
                {
                    expectedEmbedding
                }
            };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(ollamaResponse), System.Text.Encoding.UTF8, "application/json")
            };

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("/api/embed")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            var result = await service.EmbedAsync("test text");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedEmbedding, result);
        }
    }
}
