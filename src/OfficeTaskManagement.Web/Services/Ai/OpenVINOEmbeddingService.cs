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
    /// Generates text embeddings locally using OpenVINO / DirectML.
    /// Can use OpenAI embedding format or Ollama embedding format based on API type configuration.
    /// </summary>
    public class OpenVINOEmbeddingService : IGeminiEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<OpenVINOEmbeddingService> _logger;

        public OpenVINOEmbeddingService(
            HttpClient http,
            IConfiguration config,
            ILogger<OpenVINOEmbeddingService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var apiType = _config["Gemini:OpenVINOApiType"] ?? "OpenAI";
            bool isOllamaFormat = string.Equals(apiType, "Ollama", StringComparison.OrdinalIgnoreCase);

            var baseUrl = _config["Gemini:OpenVINOUrl"] ?? "http://localhost:8000/v1";
            var model = _config["Gemini:OpenVINOEmbeddingModel"] ?? _config["Gemini:OpenVINOModel"] ?? "nomic-embed-text";
            
            var timeoutSec = 600;
            if (int.TryParse(_config["Gemini:OllamaTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSec = parsedTimeout;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            if (isOllamaFormat)
            {
                var url = $"{baseUrl.TrimEnd('/')}/api/embed";
                var body = new { model = model, input = text };

                try
                {
                    using var jsonContent = new StringContent(
                        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    var response = await _http.PostAsync(url, jsonContent, cts.Token);
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
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenVINO Ollama-format embedding failed for model: {Model} at {Url}", model, url);
                    throw;
                }
            }
            else
            {
                var url = $"{baseUrl.TrimEnd('/')}/embeddings";
                var body = new { model = model, input = text };

                try
                {
                    using var jsonContent = new StringContent(
                        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    var response = await _http.PostAsync(url, jsonContent, cts.Token);
                    response.EnsureSuccessStatusCode();

                    var jsonString = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(jsonString);

                    var dataProp = doc.RootElement.GetProperty("data");
                    if (dataProp.ValueKind == JsonValueKind.Array && dataProp.GetArrayLength() > 0)
                    {
                        var firstItem = dataProp[0];
                        var embeddingProp = firstItem.GetProperty("embedding");
                        return embeddingProp.EnumerateArray()
                            .Select(v => v.GetSingle())
                            .ToArray();
                    }

                    return Array.Empty<float>();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenVINO OpenAI-format embedding failed for model: {Model} at {Url}", model, url);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Length == 0)
            {
                return Array.Empty<float[]>();
            }

            var apiType = _config["Gemini:OpenVINOApiType"] ?? "OpenAI";
            bool isOllamaFormat = string.Equals(apiType, "Ollama", StringComparison.OrdinalIgnoreCase);

            var baseUrl = _config["Gemini:OpenVINOUrl"] ?? "http://localhost:8000/v1";
            var model = _config["Gemini:OpenVINOEmbeddingModel"] ?? _config["Gemini:OpenVINOModel"] ?? "nomic-embed-text";

            var timeoutSec = 600;
            if (int.TryParse(_config["Gemini:OllamaTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSec = parsedTimeout;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            if (isOllamaFormat)
            {
                var url = $"{baseUrl.TrimEnd('/')}/api/embed";
                var body = new { model = model, input = texts };

                try
                {
                    using var jsonContent = new StringContent(
                        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    var response = await _http.PostAsync(url, jsonContent, cts.Token);
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
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenVINO Ollama-format batch embedding failed for model: {Model} at {Url}", model, url);
                    throw;
                }
            }
            else
            {
                var url = $"{baseUrl.TrimEnd('/')}/embeddings";
                var body = new { model = model, input = texts };

                try
                {
                    using var jsonContent = new StringContent(
                        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    var response = await _http.PostAsync(url, jsonContent, cts.Token);
                    response.EnsureSuccessStatusCode();

                    var jsonString = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(jsonString);

                    var results = new List<float[]>();
                    var dataProp = doc.RootElement.GetProperty("data");
                    foreach (var item in dataProp.EnumerateArray())
                    {
                        var embeddingProp = item.GetProperty("embedding");
                        var values = embeddingProp.EnumerateArray()
                            .Select(v => v.GetSingle())
                            .ToArray();
                        results.Add(values);
                    }

                    return results.ToArray();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenVINO OpenAI-format batch embedding failed for model: {Model} at {Url}", model, url);
                    throw;
                }
            }
        }
    }
}
