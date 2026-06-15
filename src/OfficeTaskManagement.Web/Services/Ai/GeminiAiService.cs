using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Models.Ai;

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
            ILogger<GeminiAiService> logger)
        {
            _http = http;
            _config = config;
            _contextBuilder = contextBuilder;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<EstimationResult> EstimateAsync(
            EstimationRequest request, CancellationToken ct = default)
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Gemini:ApiKey not configured. Returning fallback estimation.");
                return FallbackEstimation("AI estimation unavailable: API key not configured.");
            }

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
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                return new ChildItemSuggestions(request.ParentType, request.ChildType, Array.Empty<ChildItemDto>(), "API unavailable.");

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
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                return "AI unavailable. Please write acceptance criteria manually.";

            try
            {
                var prompt = $"""
                    Generate acceptance criteria for this user story in Given/When/Then format:
                    Title: {title}
                    Description: {description}
                    
                    Return 3–5 clear, testable acceptance criteria as a markdown bullet list.
                    """;

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
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                return FallbackEstimation("AI re-estimation unavailable: API key not configured.");

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
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                return new FullCascadeResult(Array.Empty<CascadeFeatureDto>());

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

        /// <summary>
        /// Calls the Gemini generateContent API with:
        ///   - Structured JSON output (response_mime_type + response_schema)
        ///   - Exponential backoff retry on 429 (2s, 4s, 8s)
        ///   - Token usage extraction
        /// </summary>
        private async Task<(string JsonText, int InputTokens, int OutputTokens)> CallGeminiApiAsync(
            string promptText, object responseSchema, string apiKey, CancellationToken ct,
            int maxRetries = 3)
        {
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
                    var delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s, 8s
                    _logger.LogWarning("Gemini 429 rate limit. Retry {Attempt}/{Max} after {Delay}ms", attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs, ct);
                }

                response = await _http.PostAsync(url,
                    new StringContent(json, Encoding.UTF8, "application/json"), ct);

                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
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
