using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Generates text embeddings using the Gemini gemini-embedding-001 model.
    /// Used by CodebaseIndexingService (Phase 3) to create vector representations
    /// of code chunks for semantic similarity search.
    /// </summary>
    public class GeminiEmbeddingService : IGeminiEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<GeminiEmbeddingService> _logger;

        public GeminiEmbeddingService(
            HttpClient http,
            IConfiguration config,
            ILogger<GeminiEmbeddingService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Gemini:ApiKey not configured. Returning empty embedding.");
                return Array.Empty<float>();
            }

            var model = _config["Gemini:EmbeddingModel"] ?? "models/gemini-embedding-001";
            var url = $"https://generativelanguage.googleapis.com/v1beta/{model}:embedContent?key={apiKey}";

            var body = new
            {
                content = new { parts = new[] { new { text } } },
                outputDimensionality = 768
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage response = null!;
            int retries = 0;
            int delayMs = 1000;
            while (true)
            {
                response = await _http.PostAsync(url, jsonContent, ct);
                var statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode == 503) && retries < 5)
                {
                    _logger.LogWarning("Gemini API returned {StatusCode}. Retrying in {Delay}ms... (Attempt {Attempt}/5)", statusCode, delayMs, retries + 1);
                    await Task.Delay(delayMs, ct);
                    retries++;
                    delayMs *= 2;
                    continue;
                }
                break;
            }
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonString);

            return doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
        }

        /// <inheritdoc/>
        public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Length == 0)
            {
                return Array.Empty<float[]>();
            }

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Gemini:ApiKey not configured. Returning empty embeddings.");
                return texts.Select(_ => Array.Empty<float>()).ToArray();
            }

            var model = _config["Gemini:EmbeddingModel"] ?? "models/gemini-embedding-001";
            var url = $"https://generativelanguage.googleapis.com/v1beta/{model}:batchEmbedContents?key={apiKey}";

            var requests = texts.Select(text => new
            {
                model,
                content = new { parts = new[] { new { text } } }
            }).ToArray();

            var body = new { requests };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage response = null!;
            int retries = 0;
            int delayMs = 1000;
            while (true)
            {
                response = await _http.PostAsync(url, jsonContent, ct);
                var statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode == 503) && retries < 5)
                {
                    _logger.LogWarning("Gemini API returned {StatusCode}. Retrying batch in {Delay}ms... (Attempt {Attempt}/5)", statusCode, delayMs, retries + 1);
                    await Task.Delay(delayMs, ct);
                    retries++;
                    delayMs *= 2;
                    continue;
                }
                break;
            }
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonString);

            var results = new List<float[]>();
            foreach (var item in doc.RootElement.GetProperty("embeddings").EnumerateArray())
            {
                var values = item.GetProperty("values")
                    .EnumerateArray()
                    .Select(v => v.GetSingle())
                    .ToArray();
                results.Add(values);
            }
            return results.ToArray();
        }
    }
}
