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

            var response = await _http.PostAsync(url, jsonContent, ct);
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
            // TODO: Replace with real batch API call when available in Gemini API
            // Currently processes sequentially; for Phase 3, batching in groups of 100
            // is handled by CodebaseIndexingService to avoid rate limits.
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await EmbedAsync(text, ct));
            }
            return results.ToArray();
        }
    }
}
