using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Generates text embeddings locally using Ollama.
    /// Can run free local models like gemma, gemma2, or nomic-embed-text.
    /// </summary>
    public class OllamaEmbeddingService : IGeminiEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<OllamaEmbeddingService> _logger;
        private readonly ILogger<GeminiEmbeddingService> _geminiLogger;

        public OllamaEmbeddingService(
            HttpClient http,
            IConfiguration config,
            ILogger<OllamaEmbeddingService> logger,
            ILogger<GeminiEmbeddingService> geminiLogger)
        {
            _http = http;
            _config = config;
            _logger = logger;
            _geminiLogger = geminiLogger;
        }

        /// <inheritdoc/>
        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var baseUrl = _config["Gemini:OllamaUrl"] ?? "http://localhost:11434";
            var model = _config["Gemini:OllamaEmbeddingModel"] ?? _config["Gemini:OllamaModel"] ?? "nomic-embed-text";
            var url = $"{baseUrl.TrimEnd('/')}/api/embed";

            var body = new
            {
                model = model,
                input = text
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            try
            {
                var response = await _http.PostAsync(url, jsonContent, ct);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonString);

                var embeddingsProp = doc.RootElement.GetProperty("embeddings");
                if (embeddingsProp.ValueKind == JsonValueKind.Array && embeddingsProp.GetArrayLength() > 0)
                {
                    var firstArray = embeddingsProp[0];
                    return firstArray.EnumerateArray()
                        .Select(v => v.GetSingle())
                        .ToArray();
                }

                return Array.Empty<float>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama embedding failed for model: {Model} at {Url}", model, url);
                var apiKey = _config["Gemini:ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Ollama failed. Falling back to Gemini embedding service.");
                    try
                    {
                        var geminiService = new GeminiEmbeddingService(_http, _config, _geminiLogger);
                        return await geminiService.EmbedAsync(text, ct);
                    }
                    catch (Exception geminiEx)
                    {
                        _logger.LogError(geminiEx, "Gemini fallback embedding also failed.");
                    }
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Length == 0)
            {
                return Array.Empty<float[]>();
            }

            var baseUrl = _config["Gemini:OllamaUrl"] ?? "http://localhost:11434";
            var model = _config["Gemini:OllamaEmbeddingModel"] ?? _config["Gemini:OllamaModel"] ?? "nomic-embed-text";
            var url = $"{baseUrl.TrimEnd('/')}/api/embed";

            var body = new
            {
                model = model,
                input = texts
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            try
            {
                var response = await _http.PostAsync(url, jsonContent, ct);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonString);

                var results = new List<float[]>();
                var embeddingsProp = doc.RootElement.GetProperty("embeddings");
                foreach (var item in embeddingsProp.EnumerateArray())
                {
                    var values = item.EnumerateArray()
                        .Select(v => v.GetSingle())
                        .ToArray();
                    results.Add(values);
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama batch embedding failed for model: {Model} at {Url}", model, url);
                var apiKey = _config["Gemini:ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Ollama failed. Falling back to Gemini embedding service.");
                    try
                    {
                        var geminiService = new GeminiEmbeddingService(_http, _config, _geminiLogger);
                        return await geminiService.EmbedBatchAsync(texts, ct);
                    }
                    catch (Exception geminiEx)
                    {
                        _logger.LogError(geminiEx, "Gemini fallback embedding also failed.");
                    }
                }
                throw;
            }
        }
    }
}
