using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Services.Codebase;


namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Core AI estimation service powered by Gemini.
    /// Implements all 5 estimation operations with:
    ///   - Structured JSON output via response_mime_type + response_schema
    ///   - Exponential backoff retry on 429 (max 3 retries: 2s/4s/8s)
    ///   - Token usage tracking (usageMetadata)
    ///   - Graceful fallback when API key is missing or call fails
    /// </summary>
    public class GeminiAiService : IGeminiAiService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ContextBuilderService _contextBuilder;
        private readonly ApplicationDbContext _db;
        private readonly CodebaseRetrievalService _codebaseRetrieval;
        private readonly ILogger<GeminiAiService> _logger;

        private const string StaticSystemPrompt = """
            You are an expert PMP-certified project manager and software architect assistant 
            for a Bangladesh-based software team. You produce structured JSON estimates.

            DOMAIN RULES:
            - Currency: BDT only. Hourly rate provided in context.
            - Working days: Sunday–Thursday (Bangladesh). Friday+Saturday = weekend.
            - Estimation method: PERT three-point (O + 4M + P) / 6.
            - Hierarchy: Project → Epic → Feature → UserStory → TaskItem.
            - Priority values: Low | Medium | High | Critical.
            - Story points: Fibonacci only (1,2,3,5,8,13,21).

            OUTPUT RULES:
            - Always return valid JSON matching the provided schema.
            - Rationale must be ≤ 2 sentences referencing actual historical data from context.
            - Never invent data not present in the context.
            - If context is insufficient, return conservative estimates with low confidence.
            - Child item titles must be unique from existing siblings listed in context.
            """;

        public GeminiAiService(
            HttpClient http,
            IConfiguration config,
            ContextBuilderService contextBuilder,
            ApplicationDbContext db,
            CodebaseRetrievalService codebaseRetrieval,
            ILogger<GeminiAiService> logger)
        {
            _http = http;
            _config = config;
            _contextBuilder = contextBuilder;
            _db = db;
            _codebaseRetrieval = codebaseRetrieval;
            _logger = logger;
        }


        /// <inheritdoc/>
        public async Task<EstimationResult> EstimateAsync(
            EstimationRequest request, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
            {
                _logger.LogWarning("Gemini:ApiKey not configured. Returning fallback estimation.");
                return FallbackEstimation("AI estimation unavailable: API key not configured.");
            }
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            try
            {
                var ctx = _contextBuilder != null
                    ? await _contextBuilder.BuildContextAsync(request, ct)
                    : new PromptContext();

                var promptText = BuildEstimationPrompt(request, ctx);

                var responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        optimisticHours    = new { type = "NUMBER" },
                        mostLikelyHours    = new { type = "NUMBER" },
                        pessimisticHours   = new { type = "NUMBER" },
                        pertHours          = new { type = "NUMBER" },
                        priority           = new { type = "STRING" },
                        storyPoints        = new { type = "INTEGER" },
                        estimatedBudgetBDT = new { type = "NUMBER" },
                        confidence         = new { type = "STRING", @enum = new[] { "High", "Medium", "Low" } },
                        rationale          = new { type = "STRING" },
                        risks              = new { type = "ARRAY", items = new { type = "STRING" } }
                    },
                    required = new[] { "optimisticHours", "mostLikelyHours", "pessimisticHours",
                                       "pertHours", "priority", "rationale", "confidence" }
                };

                var (json, inputTokens, outputTokens) = await CallGeminiApiAsync(
                    promptText, responseSchema, apiKey, ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new EstimationResult(
                    OptimisticHours:    GetDecimal(root, "optimisticHours"),
                    MostLikelyHours:    GetDecimal(root, "mostLikelyHours"),
                    PessimisticHours:   GetDecimal(root, "pessimisticHours"),
                    PertHours:          GetDecimal(root, "pertHours"),
                    Priority:           GetString(root, "priority", "Medium"),
                    StoryPoints:        GetInt(root, "storyPoints", 5),
                    EstimatedBudgetBDT: GetDecimal(root, "estimatedBudgetBDT"),
                    Confidence:         GetString(root, "confidence", "Low"),
                    Rationale:          GetString(root, "rationale", "AI estimation."),
                    Risks:              GetStringArray(root, "risks"),
                    InputTokensUsed:    inputTokens,
                    OutputTokensUsed:   outputTokens
                );
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Gemini returned malformed JSON for {EntityType} estimation", request.EntityType);
                return FallbackEstimation("AI returned an unparseable response. Using conservative estimates.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini estimation failed for {EntityType}: {Title}", request.EntityType, request.Title);
                return FallbackEstimation($"AI estimation failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ChildItemSuggestions> SuggestChildrenAsync(
            ChildRequest request, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
                return new ChildItemSuggestions(request.ParentType, request.ChildType, Array.Empty<ChildItemDto>(), "API unavailable.");
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            try
            {
                var promptText = BuildChildSuggestionPrompt(request);

                var responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        children = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    title           = new { type = "STRING" },
                                    description     = new { type = "STRING" },
                                    optimisticHours = new { type = "NUMBER" },
                                    mostLikelyHours = new { type = "NUMBER" },
                                    pessimisticHours= new { type = "NUMBER" },
                                    priority        = new { type = "STRING" }
                                },
                                required = new[] { "title", "description", "mostLikelyHours", "priority" }
                            }
                        },
                        rationale = new { type = "STRING" }
                    }
                };

                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var items = root.TryGetProperty("children", out var children)
                    ? children.EnumerateArray()
                        .Select(c => new ChildItemDto(
                            Title:              GetString(c, "title", "Untitled"),
                            Description:        GetString(c, "description", ""),
                            OptimisticHours:    GetDecimalNullable(c, "optimisticHours"),
                            MostLikelyHours:    GetDecimalNullable(c, "mostLikelyHours"),
                            PessimisticHours:   GetDecimalNullable(c, "pessimisticHours"),
                            Priority:           GetString(c, "priority", "Medium"),
                            AcceptanceCriteria: GetStringNullable(c, "acceptanceCriteria")
                        ))
                        .ToArray()
                    : Array.Empty<ChildItemDto>();

                return new ChildItemSuggestions(
                    request.ParentType,
                    request.ChildType,
                    items,
                    GetString(root, "rationale", "AI-generated suggestions."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Child suggestion failed for {ParentType}: {Title}", request.ParentType, request.ParentTitle);
                return new ChildItemSuggestions(request.ParentType, request.ChildType, Array.Empty<ChildItemDto>(), $"Failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<string> GenerateAcceptanceCriteriaAsync(
            string title, string description, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
                return "AI unavailable. Please write acceptance criteria manually.";

            var prompt = $"""
                Generate acceptance criteria for this user story in Given/When/Then format:
                Title: {title}
                Description: {description}
                
                Return 3–5 clear, testable acceptance criteria as a markdown bullet list.
                """;

            var provider = _config["Gemini:Provider"] ?? "Gemini";
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var (text, _, _) = await CallOllamaApiAsync(prompt, StaticSystemPrompt, isJson: false, ct);
                    return text;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ollama GenerateAcceptanceCriteriaAsync failed. Checking for Gemini fallback.");
                    if (string.IsNullOrEmpty(_config["Gemini:ApiKey"]))
                    {
                        throw;
                    }
                }
            }

            var apiKey = _config["Gemini:ApiKey"] ?? "";

            try
            {
                var model = _config["Gemini:GenerativeModel"] ?? "gemini-2.5-flash";
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

                var body = new
                {
                    systemInstruction = new { parts = new[] { new { text = StaticSystemPrompt } } },
                    contents = new[] { new { parts = new[] { new { text = prompt } } } }
                };

                var resp = await _http.PostAsync(url,
                    new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);

                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                return doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "Unable to generate acceptance criteria.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Acceptance criteria generation failed for: {Title}", title);
                return "Unable to generate acceptance criteria at this time.";
            }
        }

        /// <inheritdoc/>
        public async Task<EstimationResult> ReEstimateAsync(
            ReEstimationRequest request, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
                return FallbackEstimation("AI re-estimation unavailable: API key not configured.");
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            try
            {
                var estimationRequest = new EstimationRequest(
                    request.EntityType, request.Title, request.Description,
                    request.ProjectId, null, null, null);

                var ctx = _contextBuilder != null
                    ? await _contextBuilder.BuildContextAsync(estimationRequest, ct)
                    : new PromptContext();

                var promptText = $"""
                    RE-ESTIMATION REQUEST

                    Entity Type: {request.EntityType}
                    Title: {request.Title}
                    Description: {request.Description ?? "N/A"}
                    
                    ORIGINAL ESTIMATE: {request.OriginalPertHours:F1}h (PERT)
                    ACTUAL HOURS LOGGED SO FAR: {request.ActualHoursLogged?.ToString("F1") ?? "N/A"}h
                    REASON FOR RE-ESTIMATION: {request.ChangeReason ?? "Not specified"}

                    HISTORICAL CONTEXT:
                    {ctx.HistoryStats ?? "No historical data available."}

                    AVERAGE HOURLY RATE: ৳{ctx.HourlyRateBDT}/hr

                    Based on the actual hours logged and scope drift, provide a revised PERT estimate.
                    Include a rationale explaining the delta vs original estimate.
                    Return JSON only.
                    """;

                var responseSchema = BuildEstimationResponseSchema();
                var (json, inputTokens, outputTokens) = await CallGeminiApiAsync(
                    promptText, responseSchema, apiKey, ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new EstimationResult(
                    OptimisticHours:    GetDecimal(root, "optimisticHours"),
                    MostLikelyHours:    GetDecimal(root, "mostLikelyHours"),
                    PessimisticHours:   GetDecimal(root, "pessimisticHours"),
                    PertHours:          GetDecimal(root, "pertHours"),
                    Priority:           GetString(root, "priority", "Medium"),
                    StoryPoints:        GetInt(root, "storyPoints", 5),
                    EstimatedBudgetBDT: GetDecimal(root, "estimatedBudgetBDT"),
                    Confidence:         GetString(root, "confidence", "Low"),
                    Rationale:          GetString(root, "rationale", "Re-estimation."),
                    Risks:              GetStringArray(root, "risks"),
                    InputTokensUsed:    inputTokens,
                    OutputTokensUsed:   outputTokens
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Re-estimation failed for {EntityType}/{EntityId}", request.EntityType, request.EntityId);
                return FallbackEstimation($"Re-estimation failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<FullCascadeResult> GenerateFullCascadeAsync(
            FullCascadeRequest request, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
                return new FullCascadeResult(Array.Empty<CascadeFeatureDto>());
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            try
            {
                var promptText = $"""
                    FULL BREAKDOWN REQUEST

                    Epic: {request.EpicTitle}
                    Description: {request.EpicDescription ?? "N/A"}

                    Project: {request.ProjectName ?? "N/A"}

                    Generate a complete Feature → UserStory → Task breakdown.
                    Max 5 Features, max 4 UserStories per Feature, max 5 Tasks per UserStory.
                    Keep scope realistic. Return JSON.
                    """;

                var responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        features = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    title       = new { type = "STRING" },
                                    description = new { type = "STRING" },
                                    userStories = new
                                    {
                                        type = "ARRAY",
                                        items = new
                                        {
                                            type = "OBJECT",
                                            properties = new
                                            {
                                                title              = new { type = "STRING" },
                                                description        = new { type = "STRING" },
                                                acceptanceCriteria = new { type = "STRING" },
                                                mostLikelyHours    = new { type = "NUMBER" },
                                                tasks = new
                                                {
                                                    type = "ARRAY",
                                                    items = new
                                                    {
                                                        type = "OBJECT",
                                                        properties = new
                                                        {
                                                            title           = new { type = "STRING" },
                                                            optimisticHours = new { type = "NUMBER" },
                                                            mostLikelyHours = new { type = "NUMBER" },
                                                            pessimisticHours= new { type = "NUMBER" }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var features = root.TryGetProperty("features", out var featuresEl)
                    ? featuresEl.EnumerateArray()
                        .Select(f => new CascadeFeatureDto(
                            GetString(f, "title", "Feature"),
                            GetString(f, "description", ""),
                            f.TryGetProperty("userStories", out var stories)
                                ? stories.EnumerateArray()
                                    .Select(s => new CascadeUserStoryDto(
                                        GetString(s, "title", "User Story"),
                                        GetString(s, "description", ""),
                                        GetString(s, "acceptanceCriteria", ""),
                                        GetDecimal(s, "mostLikelyHours"),
                                        s.TryGetProperty("tasks", out var tasks)
                                            ? tasks.EnumerateArray()
                                                .Select(t => new CascadeTaskDto(
                                                    GetString(t, "title", "Task"),
                                                    GetDecimal(t, "optimisticHours"),
                                                    GetDecimal(t, "mostLikelyHours"),
                                                    GetDecimal(t, "pessimisticHours")
                                                ))
                                                .ToArray()
                                            : Array.Empty<CascadeTaskDto>()
                                    ))
                                    .ToArray()
                                : Array.Empty<CascadeUserStoryDto>()
                        ))
                        .ToArray()
                    : Array.Empty<CascadeFeatureDto>();

                return new FullCascadeResult(features);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Full cascade generation failed for epic: {EpicTitle}", request.EpicTitle);
                return new FullCascadeResult(Array.Empty<CascadeFeatureDto>());
            }
        }

        // ── Private Helpers ────────────────────────────────────────────────────

        private bool IsApiAvailable()
        {
            var provider = _config["Gemini:Provider"] ?? "Gemini";
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return !string.IsNullOrEmpty(_config["Gemini:ApiKey"]);
        }

        private async Task<(string JsonText, int InputTokens, int OutputTokens)> CallOllamaApiAsync(
            string promptText, string? systemPrompt, bool isJson, CancellationToken ct)
        {
            var baseUrl = _config["Gemini:OllamaUrl"] ?? "http://localhost:11434";
            var model = _config["Gemini:OllamaModel"] ?? "gemma-4-26b-a4b-it";
            var url = $"{baseUrl.TrimEnd('/')}/api/generate";

            var body = new Dictionary<string, object>
            {
                { "model", model },
                { "prompt", promptText },
                { "stream", false }
            };

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                body.Add("system", systemPrompt);
            }

            if (isJson)
            {
                body.Add("format", "json");
            }

            var timeoutSec = 600;
            if (int.TryParse(_config["Gemini:OllamaTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSec = parsedTimeout;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            try
            {
                using var jsonContent = new StringContent(
                    JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(url, jsonContent, cts.Token);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                var text = root.GetProperty("response").GetString() ?? (isJson ? "{}" : "");
                int inputTokens = root.TryGetProperty("prompt_eval_count", out var p) ? p.GetInt32() : 0;
                int outputTokens = root.TryGetProperty("eval_count", out var e) ? e.GetInt32() : 0;

                return (text, inputTokens, outputTokens);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
        }

        /// <summary>
        /// Calls the Gemini generateContent API with:
        ///   - Structured JSON output (response_mime_type + response_schema)
        ///   - Exponential backoff retry on 429 (2s, 4s, 8s)
        ///   - Token usage extraction
        /// </summary>
        private async Task<(string JsonText, int InputTokens, int OutputTokens)> CallGeminiApiAsync(
            string promptText, object responseSchema, string apiKey, CancellationToken ct,
            int maxRetries = 5)
        {
            var provider = _config["Gemini:Provider"] ?? "Gemini";
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return await CallOllamaApiAsync(promptText, StaticSystemPrompt, isJson: true, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ollama CallGeminiApiAsync failed. Checking for Gemini fallback.");
                    var backupKey = _config["Gemini:ApiKey"];
                    if (!string.IsNullOrEmpty(backupKey))
                    {
                        _logger.LogInformation("Ollama failed. Falling back to Gemini API.");
                        apiKey = backupKey;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            var model = _config["Gemini:GenerativeModel"] ?? "gemini-2.5-flash";
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var body = new
            {
                systemInstruction = new { parts = new[] { new { text = StaticSystemPrompt } } },
                contents = new[] { new { parts = new[] { new { text = promptText } } } },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema
                }
            };

            var json = JsonSerializer.Serialize(body);
            HttpResponseMessage? response = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s, 8s, 16s, 32s
                    _logger.LogWarning("Gemini rate limit or transient error. Retry {Attempt}/{Max} after {Delay}ms", attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs, ct);
                }

                response = await _http.PostAsync(url,
                    new StringContent(json, Encoding.UTF8, "application/json"), ct);

                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests &&
                    response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                    break;
            }

            response!.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Extract the JSON text from candidates[0].content.parts[0].text
            var text = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            // Extract token usage for cost monitoring
            int inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                inputTokens  = usage.TryGetProperty("promptTokenCount",     out var inp) ? inp.GetInt32() : 0;
                outputTokens = usage.TryGetProperty("candidatesTokenCount", out var out_) ? out_.GetInt32() : 0;
            }

            return (text, inputTokens, outputTokens);
        }

        private string BuildEstimationPrompt(EstimationRequest request, PromptContext ctx)
        {
            var codeContext = ctx.CodeChunks?.Any() == true
                ? string.Join("\n\n", ctx.CodeChunks)
                : "No codebase context available.";

            return $"""
                ESTIMATION REQUEST

                Entity Type: {request.EntityType}
                Title: {request.Title}
                Description: {request.Description ?? "N/A"}

                PARENT CONTEXT:
                {ctx.ParentContext ?? "No parent context."}

                EXISTING SIBLINGS (do not duplicate):
                {ctx.SiblingList ?? "No existing siblings."}

                HISTORICAL ACCURACY:
                {ctx.HistoryStats ?? "No historical data available."}

                AVERAGE HOURLY RATE: ৳{ctx.HourlyRateBDT}/hr

                CODEBASE CONTEXT (if relevant):
                {codeContext}

                Estimate this {request.EntityType} using PERT. Return JSON only.
                """;
        }

        private string BuildChildSuggestionPrompt(ChildRequest request)
        {
            return $"""
                CHILD ITEM SUGGESTION REQUEST

                Parent Type: {request.ParentType} → Child Type: {request.ChildType}
                Parent: {request.ParentTitle}
                Description: {request.ParentDescription ?? "N/A"}

                EXISTING CHILDREN (avoid duplicating):
                (Query DB for existing children — context not available here)

                Suggest {request.MinChildren}–{request.MaxChildren} {request.ChildType}s.
                Each must be distinct and non-overlapping. Return JSON only.
                """;
        }

        private static object BuildEstimationResponseSchema() => new
        {
            type = "OBJECT",
            properties = new
            {
                optimisticHours    = new { type = "NUMBER" },
                mostLikelyHours    = new { type = "NUMBER" },
                pessimisticHours   = new { type = "NUMBER" },
                pertHours          = new { type = "NUMBER" },
                priority           = new { type = "STRING" },
                storyPoints        = new { type = "INTEGER" },
                estimatedBudgetBDT = new { type = "NUMBER" },
                confidence         = new { type = "STRING", @enum = new[] { "High", "Medium", "Low" } },
                rationale          = new { type = "STRING" },
                risks              = new { type = "ARRAY", items = new { type = "STRING" } }
            },
            required = new[] { "optimisticHours", "mostLikelyHours", "pessimisticHours",
                               "pertHours", "priority", "rationale", "confidence" }
        };

        private static EstimationResult FallbackEstimation(string reason) =>
            new EstimationResult(
                OptimisticHours:    4m,
                MostLikelyHours:    8m,
                PessimisticHours:   16m,
                PertHours:          9m,
                Priority:           "Medium",
                StoryPoints:        5,
                EstimatedBudgetBDT: 0m,
                Confidence:         "Low",
                Rationale:          reason,
                Risks:              new[] { "AI estimation unavailable — manual review required" },
                InputTokensUsed:    0,
                OutputTokensUsed:   0
             );

        /// <inheritdoc/>
        public async Task<ProjectAnalysisResult> AnalyzeProjectCodebaseAsync(int projectId, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
            {
                return new ProjectAnalysisResult(
                    "AI Analysis unavailable (no API Key).",
                    "N/A",
                    "N/A",
                    true,
                    new[] { new EpicSuggestionDto("Default Module", "Basic features logic") }
                );
            }
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            var project = await _db.Projects.FindAsync(new object[] { projectId }, ct);
            if (project == null)
            {
                throw new ArgumentException($"Project with ID {projectId} not found.", nameof(projectId));
            }

            var repoPath = project.RepositoryPath;
            if (string.IsNullOrEmpty(repoPath))
            {
                repoPath = ".";
            }

            var actualPath = repoPath;
            if (!Path.IsPathRooted(actualPath))
            {
                actualPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), actualPath));
            }

            _logger.LogInformation("Analyzing project codebase for onboarding. Project: {ProjectName}, Root: {Root}", project.Name, actualPath);

            var repoScanText = await ScanCodebaseStructureAsync(actualPath, ct);

            var promptText = $"""
                You are a master PMP and software architect. We are onboarding an existing code repository to our project management system.
                Here is the structural scanning of the codebase:

                {repoScanText}

                Based on this scan, please analyze the project codebase and provide suggestions:
                1. A clear high-level summary of what this project does.
                2. The technology stack, framework, and language details.
                3. An overview of the test coverage. State if unit or integration tests are absent or incomplete. Set testsAbsentOrIncomplete=true if tests are absent or look very sparse/non-comprehensive.
                4. Suggest 3 to 6 logical Epics (high-level system modules/domains) to initiate the task backlog.

                Return JSON only.
                """;

            var responseSchema = new
            {
                type = "OBJECT",
                properties = new
                {
                    projectSummary = new { type = "STRING" },
                    techStack = new { type = "STRING" },
                    testOverview = new { type = "STRING" },
                    testsAbsentOrIncomplete = new { type = "BOOLEAN" },
                    suggestedEpics = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                name = new { type = "STRING" },
                                description = new { type = "STRING" }
                            },
                            required = new[] { "name", "description" }
                        }
                    }
                },
                required = new[] { "projectSummary", "techStack", "testOverview", "testsAbsentOrIncomplete", "suggestedEpics" }
            };

            try
            {
                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var epics = root.GetProperty("suggestedEpics").EnumerateArray()
                    .Select(e => new EpicSuggestionDto(
                        GetString(e, "name", "Module"),
                        GetString(e, "description", "")
                    ))
                    .ToArray();

                return new ProjectAnalysisResult(
                    GetString(root, "projectSummary", "Scanned codebase."),
                    GetString(root, "techStack", "Undetermined"),
                    GetString(root, "testOverview", "No tests scanned."),
                    root.TryGetProperty("testsAbsentOrIncomplete", out var tai) && tai.ValueKind == JsonValueKind.True,
                    epics
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Codebase onboarding analysis failed for project {ProjectId}", projectId);
                return new ProjectAnalysisResult(
                    "Failed to analyze repository. " + ex.Message,
                    "N/A",
                    "N/A",
                    true,
                    new[] { new EpicSuggestionDto("Core Functionality", "General module for the project features") }
                );
            }
        }

        /// <inheritdoc/>
        public async Task<List<FeatureSuggestionDto>> SuggestFeaturesForEpicAsync(int projectId, string epicName, string epicDescription, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
            {
                return new List<FeatureSuggestionDto> { new FeatureSuggestionDto("Core features", "Standard epic logic") };
            }
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            // RAG codebase query
            var query = $"Epic: {epicName} {epicDescription}";
            var chunks = await _codebaseRetrieval.GetRelevantChunksAsync(query, projectId, topK: 3, ct);
            var codeContext = chunks.Any() ? string.Join("\n\n", chunks) : "No specific code chunks found.";

            var promptText = $"""
                Epic Name: {epicName}
                Epic Description: {epicDescription}

                CODEBASE CONTEXT FROM RELEVANT FILES:
                {codeContext}

                Suggest 3 to 5 Features that belong under this Epic, grounded in the codebase context provided above.
                Return JSON only.
                """;

            var responseSchema = new
            {
                type = "OBJECT",
                properties = new
                {
                    features = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                name = new { type = "STRING" },
                                description = new { type = "STRING" }
                            },
                            required = new[] { "name", "description" }
                        }
                    }
                },
                required = new[] { "features" }
            };

            try
            {
                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return root.GetProperty("features").EnumerateArray()
                    .Select(f => new FeatureSuggestionDto(
                        GetString(f, "name", "Feature"),
                        GetString(f, "description", "")
                    ))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestFeaturesForEpic failed for Epic {EpicName}", epicName);
                return new List<FeatureSuggestionDto> { new FeatureSuggestionDto("General Implementation", "Implementation of the epic features") };
            }
        }

        /// <inheritdoc/>
        public async Task<List<UserStorySuggestionDto>> SuggestUserStoriesForFeatureAsync(int projectId, string epicName, string featureName, string featureDescription, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
            {
                return new List<UserStorySuggestionDto> { new UserStorySuggestionDto("As a user...", "Basic user story", "Given/When/Then", "Medium") };
            }
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            var query = $"Feature: {featureName} {featureDescription}";
            var chunks = await _codebaseRetrieval.GetRelevantChunksAsync(query, projectId, topK: 3, ct);
            var codeContext = chunks.Any() ? string.Join("\n\n", chunks) : "No specific code chunks found.";

            var promptText = $"""
                Epic: {epicName}
                Feature Name: {featureName}
                Feature Description: {featureDescription}

                CODEBASE CONTEXT FROM RELEVANT FILES:
                {codeContext}

                Suggest 2 to 4 User Stories for this Feature.
                For each story, provide:
                - Title (in standard 'As a... I want to... So that...' or descriptive form)
                - Description
                - AcceptanceCriteria in clear Given/When/Then format.
                - Priority (Low, Medium, High, Critical)

                Return JSON only.
                """;

            var responseSchema = new
            {
                type = "OBJECT",
                properties = new
                {
                    stories = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                title = new { type = "STRING" },
                                description = new { type = "STRING" },
                                acceptanceCriteria = new { type = "STRING" },
                                priority = new { type = "STRING", @enum = new[] { "Low", "Medium", "High", "Critical" } }
                            },
                            required = new[] { "title", "description", "acceptanceCriteria", "priority" }
                        }
                    }
                },
                required = new[] { "stories" }
            };

            try
            {
                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return root.GetProperty("stories").EnumerateArray()
                    .Select(s => new UserStorySuggestionDto(
                        GetString(s, "title", "User Story"),
                        GetString(s, "description", ""),
                        GetString(s, "acceptanceCriteria", ""),
                        GetString(s, "priority", "Medium")
                    ))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestUserStoriesForFeature failed for Feature {FeatureName}", featureName);
                return new List<UserStorySuggestionDto>
                {
                    new UserStorySuggestionDto($"Implement {featureName}", "General implementation story", "Given validation succeeds, When feature runs, Then it works", "Medium")
                };
            }
        }

        /// <inheritdoc/>
        public async Task<TaskAndTestCaseSuggestionsDto> SuggestTasksAndTestCasesAsync(int projectId, string storyTitle, string storyDescription, bool suggestTests, CancellationToken ct = default)
        {
            if (!IsApiAvailable())
            {
                return new TaskAndTestCaseSuggestionsDto(
                    new[] { new TaskSuggestionDto("Implement story", "Main task", 4m, 8m, 16m, "Medium") },
                    new[] { new TestCaseSuggestionDto("Verify logic", "Execute feature", "Runs correctly") }
                );
            }
            var apiKey = _config["Gemini:ApiKey"] ?? "";

            var query = $"Story: {storyTitle} {storyDescription}";
            var chunks = await _codebaseRetrieval.GetRelevantChunksAsync(query, projectId, topK: 3, ct);
            var codeContext = chunks.Any() ? string.Join("\n\n", chunks) : "No specific code chunks found.";

            var testInstruct = suggestTests
                ? "TEST GAP DETECTED: Unit tests are absent or incomplete for this area. You MUST include QA/test creation tasks (e.g. 'Write xUnit tests', 'Integrate integration tests') and highly comprehensive test cases."
                : "Include standard implementation tasks and functional verification test cases.";

            var promptText = $"""
                User Story: {storyTitle}
                Description: {storyDescription}

                CODEBASE CONTEXT FROM RELEVANT FILES:
                {codeContext}

                {testInstruct}

                Suggest:
                1. 2 to 4 engineering Tasks. For each task, estimate Optimistic, Most Likely, and Pessimistic hours. Specify task Priority.
                2. 1 to 3 QA Test Cases. For each, give a clear Title, step-by-step Steps, and ExpectedResult.

                Return JSON only.
                """;

            var responseSchema = new
            {
                type = "OBJECT",
                properties = new
                {
                    tasks = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                title = new { type = "STRING" },
                                description = new { type = "STRING" },
                                optimisticHours = new { type = "NUMBER" },
                                mostLikelyHours = new { type = "NUMBER" },
                                pessimisticHours = new { type = "NUMBER" },
                                priority = new { type = "STRING", @enum = new[] { "Low", "Medium", "High", "Critical" } }
                            },
                            required = new[] { "title", "description", "optimisticHours", "mostLikelyHours", "pessimisticHours", "priority" }
                        }
                    },
                    testCases = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                title = new { type = "STRING" },
                                steps = new { type = "STRING" },
                                expectedResult = new { type = "STRING" }
                            },
                            required = new[] { "title", "steps", "expectedResult" }
                        }
                    }
                },
                required = new[] { "tasks", "testCases" }
            };

            try
            {
                var (json, _, _) = await CallGeminiApiAsync(promptText, responseSchema, apiKey, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tasks = root.GetProperty("tasks").EnumerateArray()
                    .Select(t => new TaskSuggestionDto(
                        GetString(t, "title", "Task"),
                        GetString(t, "description", ""),
                        GetDecimal(t, "optimisticHours", 4m),
                        GetDecimal(t, "mostLikelyHours", 8m),
                        GetDecimal(t, "pessimisticHours", 16m),
                        GetString(t, "priority", "Medium")
                    ))
                    .ToArray();

                var testCases = root.GetProperty("testCases").EnumerateArray()
                    .Select(tc => new TestCaseSuggestionDto(
                        GetString(tc, "title", "Test Case"),
                        GetString(tc, "steps", ""),
                        GetString(tc, "expectedResult", "")
                    ))
                    .ToArray();

                return new TaskAndTestCaseSuggestionsDto(tasks, testCases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuggestTasksAndTestCases failed for story {StoryTitle}", storyTitle);
                return new TaskAndTestCaseSuggestionsDto(
                    new[] { new TaskSuggestionDto($"Develop {storyTitle}", "Engineering task", 4m, 8m, 16m, "Medium") },
                    new[] { new TestCaseSuggestionDto($"Verify {storyTitle}", "Run validation", "Works successfully") }
                );
            }
        }

        private async Task<string> ScanCodebaseStructureAsync(string repoPath, CancellationToken ct)

        {
            var sb = new StringBuilder();
            if (!Directory.Exists(repoPath))
            {
                return "Directory does not exist.";
            }

            sb.AppendLine($"Repository Root: {repoPath}");

            // Read README.md if present
            var readmePath = Path.Combine(repoPath, "README.md");
            if (!File.Exists(readmePath))
            {
                readmePath = Path.Combine(repoPath, "readme.md");
            }
            if (File.Exists(readmePath))
            {
                try
                {
                    var readmeContent = await File.ReadAllTextAsync(readmePath, ct);
                    var snippet = readmeContent.Length > 2000 ? readmeContent[..2000] + "..." : readmeContent;
                    sb.AppendLine("\n--- README.md Content Snippet ---");
                    sb.AppendLine(snippet);
                    sb.AppendLine("---------------------------------\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Error reading README.md: {ex.Message}]");
                }
            }

            // Scan directory structure (max 3 levels deep)
            sb.AppendLine("Directory Structure (up to 3 levels, ignoring bin/obj/node_modules/.git/.vs/Migrations):");
            ScanDirectory(repoPath, repoPath, sb, 0, maxDepth: 3);

            // Scan test coverage indicator
            var hasTests = Directory.EnumerateFiles(repoPath, "*.*", SearchOption.AllDirectories)
                .Any(f => f.Contains("Test", StringComparison.OrdinalIgnoreCase) || f.Contains("spec", StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"\nTest files detection indicator: {(hasTests ? "Found test files in repository." : "No files containing 'Test' or 'spec' detected in repository.")}");

            return sb.ToString();
        }

        private void ScanDirectory(string root, string current, StringBuilder sb, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            var skipDirs = new[] { "bin", "obj", "node_modules", ".git", ".vs", "Migrations", "wwwroot", "uploads", "Debug", "Properties" };

            try
            {
                var dirs = Directory.GetDirectories(current)
                    .Select(d => Path.GetFileName(d))
                    .Where(name => !skipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var files = Directory.GetFiles(current)
                    .Select(f => Path.GetFileName(f))
                    .Where(name => !name.StartsWith(".") && !name.EndsWith(".user") && !name.EndsWith(".suo"))
                    .Take(15) // limit files per dir in scan to avoid huge context
                    .ToList();

                var indent = new string(' ', depth * 2);

                foreach (var dir in dirs)
                {
                    sb.AppendLine($"{indent}📁 {dir}/");
                    ScanDirectory(root, Path.Combine(current, dir), sb, depth + 1, maxDepth);
                }

                foreach (var file in files)
                {
                    sb.AppendLine($"{indent}📄 {file}");
                }
            }
            catch
            {
                // Ignore folder reading errors
            }
        }

        // ── JSON Extraction Helpers ───────────────────────────────────────────

        private static decimal GetDecimal(JsonElement el, string prop, decimal defaultVal = 0m)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
               ? v.GetDecimal() : defaultVal;

        private static decimal? GetDecimalNullable(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
               ? v.GetDecimal() : null;

        private static int GetInt(JsonElement el, string prop, int defaultVal = 0)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
               ? v.GetInt32() : defaultVal;

        private static string GetString(JsonElement el, string prop, string defaultVal = "")
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
               ? v.GetString() ?? defaultVal : defaultVal;

        private static string? GetStringNullable(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
               ? v.GetString() : null;

        private static string[] GetStringArray(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
               ? arr.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()!)
                    .ToArray()
               : Array.Empty<string>();
    }
}
