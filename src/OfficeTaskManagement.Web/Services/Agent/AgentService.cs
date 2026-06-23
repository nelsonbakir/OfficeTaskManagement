using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;

namespace OfficeTaskManagement.Services.Agent;

/// <summary>
/// Multi-turn AI Copilot service. Manages conversation history, builds PM context,
/// calls Gemini with function calling enabled, dispatches tool calls agentic-loop style,
/// and returns a final text response with optional UI action suggestions.
/// Spec: ai-agent-plan/05_SERVICE_LAYER.md → AgentService (Phase 4)
/// </summary>
public class AgentService : IAgentService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly AgentConversationService _conversationService;
    private readonly AgentToolDispatcher _dispatcher;
    private readonly ContextBuilderService _contextBuilder;
    private readonly ILogger<AgentService> _logger;
    private readonly MentionContextResolver _mentionResolver;

    private const int MaxFunctionCallRounds = 5; // prevent infinite agentic loops

    public AgentService(
        HttpClient http,
        IConfiguration config,
        AgentConversationService conversationService,
        AgentToolDispatcher dispatcher,
        ContextBuilderService contextBuilder,
        ILogger<AgentService> logger,
        MentionContextResolver mentionResolver)
    {
        _http = http;
        _config = config;
        _conversationService = conversationService;
        _dispatcher = dispatcher;
        _contextBuilder = contextBuilder;
        _logger = logger;
        _mentionResolver = mentionResolver;
    }

    // ── ChatAsync — multi-turn agentic loop ────────────────────────────────────
    public async Task<AgentChatResponse> ChatAsync(
        AgentChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _config["Gemini:ApiKey"];
        var model  = _config["Gemini:CopilotModel"] ?? "gemini-2.5-pro";
        var provider = _config["Gemini:Provider"] ?? "Gemini";
        bool isOllama = string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase);

        if (!isOllama && string.IsNullOrEmpty(apiKey))
        {
            return new AgentChatResponse(
                request.ConversationId,
                "AI Copilot is not configured. Please add a Gemini API key in settings.",
                null);
        }

        // 1. Load conversation history
        var tenantId = request.TenantId;  // BUG 2 FIX: use real tenant from ClaimsPrincipal
        var conversation = await _conversationService.GetOrCreateAsync(
            request.ConversationId, request.UserId, tenantId,
            request.EntityType, request.EntityId, ct);

        var history = await _conversationService.GetTurnsAsync(request.ConversationId, ct);

        // 2. Build system instruction with PM context snapshot
        var systemInstruction = await BuildSystemInstructionAsync(request, ct);

        // 3. Append the new user message to history
        await _conversationService.AppendTurnAsync(request.ConversationId, "user", request.Message, ct);

        // 4. Prepare Gemini request with tools + full history
        var contents = BuildContents(history, request.Message);

        var requestBody = new
        {
            system_instruction = new { parts = new[] { new { text = systemInstruction } } },
            contents,
            tools = AgentToolDefinitions.GetTools(),
            tool_config = new { function_calling_config = new { mode = "AUTO" } },
            generation_config = new
            {
                temperature = 0.3,
                max_output_tokens = 2048
            }
        };

        // 5. Agentic loop — keep dispatching function calls until text response
        var currentBody = requestBody as object;
        string? finalText = null;
        var suggestedActions = new List<AgentAction>();
        int rounds = 0;
        bool apiFailed = false;

        while (rounds < MaxFunctionCallRounds)
        {
            rounds++;
            var responseJson = await CallGeminiAsync(model, apiKey, currentBody, ct);
            if (responseJson == null)
            {
                apiFailed = true;
                break;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var candidates = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts");

            // Check for function call
            bool hasFunctionCall = false;
            var functionResponses = new List<object>();

            foreach (var part in candidates.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var fc))
                {
                    hasFunctionCall = true;
                    var funcName = fc.GetProperty("name").GetString() ?? "";
                    var funcArgs = fc.GetProperty("args");

                    _logger.LogInformation("Copilot dispatching: {Name}", funcName);
                    var result = await _dispatcher.DispatchAsync(
                        funcName, funcArgs, request.UserId, tenantId, ct);

                    // Track createable actions for UI buttons
                    if (funcName.StartsWith("create_"))
                    {
                        suggestedActions.Add(new AgentAction(funcName, result, new { }));
                    }

                    functionResponses.Add(new
                    {
                        functionResponse = new
                        {
                            name     = funcName,
                            response = new { result }
                        }
                    });
                }
                else if (part.TryGetProperty("text", out var text))
                {
                    finalText = text.GetString();
                }
            }

            if (!hasFunctionCall)
                break;

            // Build next turn with function responses
            currentBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                contents = BuildContentsWithFunctionResponses(contents, candidates, functionResponses),
                tools = AgentToolDefinitions.GetTools(),
                tool_config = new { function_calling_config = new { mode = "AUTO" } },
                generation_config = new { temperature = 0.3, max_output_tokens = 2048 }
            };
        }

        if (finalText == null)
        {
            if (apiFailed)
            {
                finalText = "⚠ Encountered an issue communicating with the AI service. Please check your Ollama configuration or Gemini API key / rate limits.";
            }
            else
            {
                finalText = "I processed your request. " +
                            (suggestedActions.Any()
                                ? $"{suggestedActions.Count} action(s) were completed."
                                : "How else can I help?");
            }
        }

        // 6. Persist the model's reply
        await _conversationService.AppendTurnAsync(request.ConversationId, "model", finalText, ct);

        return new AgentChatResponse(
            request.ConversationId,
            finalText,
            suggestedActions.Count > 0 ? suggestedActions.ToArray() : null);
    }

    public async Task ClearConversationAsync(
        string conversationId, string userId, CancellationToken ct = default)
    {
        await _conversationService.DeleteAsync(conversationId, ct);
    }

    // ── StreamChatAsync — KF-1 Streaming ──────────────────────────────────────
    // Runs the full agentic function-call loop first (non-streaming, handles tool calls),
    // then streams the FINAL text response either via Gemini SSE or fallback chunking.
    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Phase 1 — Run the complete agentic loop to handle all function calls.
        // We reuse ChatAsync internals so all tool dispatch + history persistence
        // happen identically. Once ChatAsync returns we know the final answer text.
        // NOTE: yield return is not allowed inside catch blocks (CS1631), so we
        // capture the error message in a local variable and yield it after the try-catch.
        AgentChatResponse? response = null;
        string? phase1Error = null;
        bool cancelled = false;
        try
        {
            response = await ChatAsync(request, ct);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StreamChatAsync: ChatAsync phase failed");
            phase1Error = "⚠ An error occurred while processing your request.";
        }

        if (cancelled) yield break;
        if (phase1Error != null) { yield return phase1Error; yield break; }

        var finalText = response!.Message;
        if (string.IsNullOrEmpty(finalText))
        {
            yield break;
        }

        // Phase 2 — Stream the final text response.
        // Try to use Gemini streamGenerateContent SSE for the LAST turn so the client
        // receives incremental tokens.  Fall back to chunked delivery if unavailable.
        var apiKey  = _config["Gemini:ApiKey"];
        var model   = _config["Gemini:CopilotModel"] ?? "gemini-2.5-pro";
        var provider = _config["Gemini:Provider"] ?? "Gemini";
        bool isOllama = string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase);

        bool streamed = false;

        if (!isOllama && !string.IsNullOrEmpty(apiKey))
        {
            // Build a single-turn request with just the confirmed final text as a
            // "paraphrase / continue" prompt so we can stream the real tokens.
            // For simplicity and to avoid double tool-call loops, we ask Gemini to
            // stream the already-computed answer verbatim.
            //
            // TODO: For true latency savings, integrate Gemini SSE directly into the
            //       agentic loop so the last model turn is natively streamed rather
            //       than re-requested. That requires refactoring CallGeminiAsync to
            //       return a Stream and plumbing IAsyncEnumerable through the loop.
            var streamUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

            // We ask Gemini to repeat the answer — keeps the stream contract intact
            // without re-running tool calls.
            var singleTurnBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role  = "user",
                        parts = new[] { new { text = $"Please repeat the following PM assistant answer verbatim:\n\n{finalText}" } }
                    }
                },
                generation_config = new { temperature = 0.0, max_output_tokens = 4096 }
            };

            // Collect SSE chunks outside try-catch so we can yield without CS1626 restriction.
            // The list approach is safe because Gemini SSE lines are small text fragments.
            var sseChunks = new List<string>();
            bool sseSuccess = false;
            bool sseCancelled = false;
            try
            {
                var json  = JsonSerializer.Serialize(singleTurnBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, streamUrl) { Content = content };
                using var httpResp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

                if (httpResp.IsSuccessStatusCode)
                {
                    await using var sseStream = await httpResp.Content.ReadAsStreamAsync(ct);
                    using var sseReader = new System.IO.StreamReader(sseStream);

                    while (!sseReader.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await sseReader.ReadLineAsync(ct);
                        if (line == null) break;
                        if (!line.StartsWith("data: ")) continue;

                        var dataStr = line["data: ".Length..].Trim();
                        if (dataStr == "[DONE]") break;
                        if (string.IsNullOrEmpty(dataStr)) continue;

                        try
                        {
                            using var chunkDoc = JsonDocument.Parse(dataStr);
                            var text = chunkDoc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

                            if (!string.IsNullOrEmpty(text))
                            {
                                sseChunks.Add(text);
                                sseSuccess = true;
                            }
                        }
                        catch (JsonException)
                        {
                            // Malformed SSE chunk — skip gracefully
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                sseCancelled = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StreamChatAsync: Gemini SSE stream failed, falling back to chunked delivery");
            }

            if (sseCancelled) yield break;

            // Yield collected SSE chunks outside the catch block
            foreach (var chunk in sseChunks)
            {
                yield return chunk;
            }
            streamed = sseSuccess;
        }

        // Phase 3 — Fallback: chunk the pre-computed response text (~50 chars per chunk)
        // so the ReadableStream frontend pattern still works without true SSE.
        if (!streamed)
        {
            const int chunkSize = 50;
            for (int i = 0; i < finalText.Length && !ct.IsCancellationRequested; i += chunkSize)
            {
                var end   = Math.Min(i + chunkSize, finalText.Length);
                yield return finalText[i..end];
                await Task.Delay(10, ct); // small delay to simulate token flow
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    // ── BUG 5 FIX: Wire ContextBuilderService for live PM context ─────────────
    private async Task<string> BuildSystemInstructionAsync(
        AgentChatRequest request, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert AI Project Management Copilot for OfficeTaskManagement.");
        sb.AppendLine("Help the user manage their projects using PERT three-point estimation (O+4M+P)/6.");
        sb.AppendLine("Weekend is Friday+Saturday in Bangladesh. Currency is BDT.");
        sb.AppendLine("When creating items, always use the provided tools — never just describe what to do.");
        sb.AppendLine("Keep responses concise. Use markdown formatting.");
        sb.AppendLine("When referring to any existing Project, Epic, Feature, UserStory, Task, Sprint, or User, ALWAYS format it as @Type:Id:Name (e.g. @Epic:12:Authentication, @User:abc-123-xyz:John Doe, @Task:45:Setup DB). This allows the UI to render clickable links for the user.");

        // Inject live entity context from DB when an entity page is active
        if (!string.IsNullOrEmpty(request.EntityType) && request.EntityId.HasValue)
        {
            sb.AppendLine($"\nCurrent context: {request.EntityType} ID={request.EntityId}");

            try
            {
                // Build EstimationRequest using positional constructor, resolving the
                // correct parent ID field based on the active entity type.
                int? projectId   = null, epicId = null, featureId = null, userStoryId = null;
                switch (request.EntityType)
                {
                    case "Project":   projectId   = request.EntityId; break;
                    case "Epic":      epicId      = request.EntityId; break;
                    case "Feature":   featureId   = request.EntityId; break;
                    case "UserStory": userStoryId = request.EntityId; break;
                    case "Task":      userStoryId = request.EntityId; break;
                }

                var estRequest = new OfficeTaskManagement.Models.Ai.EstimationRequest(
                    EntityType:  request.EntityType ?? "Project",
                    Title:       $"{request.EntityType} #{request.EntityId}",
                    Description: null,
                    ProjectId:   projectId,
                    EpicId:      epicId,
                    FeatureId:   featureId,
                    UserStoryId: userStoryId
                );

                var ctx = await _contextBuilder.BuildContextAsync(estRequest, ct);

                if (!string.IsNullOrEmpty(ctx.ParentContext))
                    sb.AppendLine($"\n### Parent\n{ctx.ParentContext}");

                if (!string.IsNullOrEmpty(ctx.SiblingList))
                    sb.AppendLine($"\n### Siblings\n{ctx.SiblingList}");

                if (!string.IsNullOrEmpty(ctx.HistoryStats))
                    sb.AppendLine($"\n### Historical Stats\n{ctx.HistoryStats}");

                if (ctx.HourlyRateBDT > 0)
                    sb.AppendLine($"\nAverage hourly rate: {ctx.HourlyRateBDT} BDT/hr");

                if (ctx.CodeChunks?.Count > 0)      // IReadOnlyList — use .Count not .Length
                {
                    sb.AppendLine("\n### Relevant Codebase Sections");
                    foreach (var chunk in ctx.CodeChunks)
                        sb.AppendLine($"```\n{chunk}\n```");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ContextBuilder failed for copilot — proceeding without live context");
            }
        }
        else if (request.ProjectContextId.HasValue)
        {
            sb.AppendLine($"\nCurrent active project context: Project ID={request.ProjectContextId.Value}");
            try
            {
                var estRequest = new OfficeTaskManagement.Models.Ai.EstimationRequest(
                    EntityType:  "Project",
                    Title:       $"Project #{request.ProjectContextId.Value}",
                    Description: null,
                    ProjectId:   request.ProjectContextId.Value,
                    EpicId:      null,
                    FeatureId:   null,
                    UserStoryId: null
                );

                var ctx = await _contextBuilder.BuildContextAsync(estRequest, ct);

                if (!string.IsNullOrEmpty(ctx.ParentContext))
                    sb.AppendLine($"\n### Project Context\n{ctx.ParentContext}");

                if (!string.IsNullOrEmpty(ctx.SiblingList))
                    sb.AppendLine($"\n### Epics in this Project\n{ctx.SiblingList}");

                if (!string.IsNullOrEmpty(ctx.HistoryStats))
                    sb.AppendLine($"\n### Historical Stats\n{ctx.HistoryStats}");

                if (ctx.HourlyRateBDT > 0)
                    sb.AppendLine($"\nAverage hourly rate: {ctx.HourlyRateBDT} BDT/hr");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ContextBuilder failed for copilot project context — proceeding without context");
            }
        }

        if (request.Mentions != null && request.Mentions.Length > 0)
        {
            sb.AppendLine("\n## Referenced Items (@mentions)");
            var resolved = await _mentionResolver.ResolveAsync(request.Mentions, ct);
            foreach (var block in resolved)
            {
                sb.AppendLine(block);
            }
        }

        return sb.ToString();
    }

    private static object[] BuildContents(
        IReadOnlyList<ConversationTurn> history, string newMessage)
    {
        var contents = new List<object>();

        foreach (var turn in history)
        {
            contents.Add(new
            {
                role  = turn.Role == "user" ? "user" : "model",
                parts = new[] { new { text = turn.Text } }
            });
        }

        // Add the current user message (already appended to DB above)
        contents.Add(new
        {
            role  = "user",
            parts = new[] { new { text = newMessage } }
        });

        return contents.ToArray();
    }

    // ── BUG 3 FIX: Preserve original JsonElement parts verbatim ───────────────
    // The Gemini API requires functionCall parts to be echoed back exactly as returned.
    // Projecting them to { text: "" } loses the function call, breaking multi-round loops.
    private static object[] BuildContentsWithFunctionResponses(
        object[] previousContents,
        JsonElement modelParts,
        List<object> functionResponses)
    {
        var contents = previousContents.ToList();

        // Reconstruct the model turn faithfully — include both text AND functionCall parts
        var reconstructedParts = modelParts.EnumerateArray().Select(p =>
        {
            if (p.TryGetProperty("functionCall", out var fc))
            {
                return (object)new
                {
                    functionCall = new
                    {
                        name = fc.GetProperty("name").GetString(),
                        args = fc.GetProperty("args").Clone()
                    }
                };
            }
            // text part
            return (object)new { text = p.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "" };
        }).ToArray();

        contents.Add(new
        {
            role  = "model",
            parts = reconstructedParts
        });

        // Append function response(s) as a user turn
        contents.Add(new
        {
            role  = "user",
            parts = functionResponses.ToArray()
        });

        return contents.ToArray();
    }

    private async Task<string?> CallOllamaChatAsync(object body, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            using var doc = JsonDocument.Parse(json);

            string systemInstruction = "";
            if (doc.RootElement.TryGetProperty("system_instruction", out var siProp) &&
                siProp.TryGetProperty("parts", out var partsProp) &&
                partsProp.ValueKind == JsonValueKind.Array &&
                partsProp.GetArrayLength() > 0)
            {
                systemInstruction = partsProp[0].GetProperty("text").GetString() ?? "";
            }

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                messages.Add(new { role = "system", content = systemInstruction });
            }

            if (doc.RootElement.TryGetProperty("contents", out var contentsProp) &&
                contentsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var contentItem in contentsProp.EnumerateArray())
                {
                    var role = contentItem.GetProperty("role").GetString() ?? "user";
                    var ollamaRole = role == "model" ? "assistant" : "user";

                    if (contentItem.TryGetProperty("parts", out var partsArray) &&
                        partsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in partsArray.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp))
                            {
                                var text = textProp.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    messages.Add(new { role = ollamaRole, content = text });
                                }
                            }
                            else if (part.TryGetProperty("functionCall", out var fcProp))
                            {
                                var name = fcProp.GetProperty("name").GetString() ?? "";
                                var args = fcProp.GetProperty("args").Clone();
                                messages.Add(new
                                {
                                    role = "assistant",
                                    tool_calls = new[]
                                    {
                                        new
                                        {
                                            type = "function",
                                            function = new
                                            {
                                                name = name,
                                                arguments = args
                                            }
                                        }
                                    }
                                });
                            }
                            else if (part.TryGetProperty("functionResponse", out var frProp))
                            {
                                var responseObj = frProp.GetProperty("response").Clone();
                                messages.Add(new
                                {
                                    role = "tool",
                                    content = JsonSerializer.Serialize(responseObj)
                                });
                            }
                        }
                    }
                }
            }

            var ollamaTools = new List<object>();
            if (doc.RootElement.TryGetProperty("tools", out var toolsProp) &&
                toolsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolItem in toolsProp.EnumerateArray())
                {
                    if (toolItem.TryGetProperty("function_declarations", out var fdProp) &&
                        fdProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var decl in fdProp.EnumerateArray())
                        {
                            ollamaTools.Add(new
                            {
                                type = "function",
                                function = decl.Clone()
                            });
                        }
                    }
                }
            }

            var baseUrl = _config["Gemini:OllamaUrl"] ?? "http://localhost:11434";
            var model = _config["Gemini:OllamaModel"] ?? "gemma-4-26b-a4b-it";
            var url = $"{baseUrl.TrimEnd('/')}/api/chat";

            var ollamaRequestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messages },
                { "stream", false }
            };

            if (ollamaTools.Count > 0)
            {
                ollamaRequestBody.Add("tools", ollamaTools);
            }

            if (doc.RootElement.TryGetProperty("generation_config", out var genConfig))
            {
                var options = new Dictionary<string, object>();
                if (genConfig.TryGetProperty("temperature", out var tempProp))
                {
                    options.Add("temperature", tempProp.GetDouble());
                }
                if (options.Count > 0)
                {
                    ollamaRequestBody.Add("options", options);
                }
            }

            var jsonString = JsonSerializer.Serialize(ollamaRequestBody);

            var timeoutSec = 600;
            if (int.TryParse(_config["Gemini:OllamaTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSec = parsedTimeout;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            using var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, httpContent, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var respDoc = JsonDocument.Parse(responseBody);
            var messageProp = respDoc.RootElement.GetProperty("message");
            var content = messageProp.TryGetProperty("content", out var cProp) ? cProp.GetString() : null;

            var geminiParts = new List<object>();
            if (!string.IsNullOrEmpty(content))
            {
                geminiParts.Add(new { text = content });
            }

            if (messageProp.TryGetProperty("tool_calls", out var toolCallsProp) &&
                toolCallsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsProp.EnumerateArray())
                {
                    if (tc.TryGetProperty("function", out var funcProp))
                    {
                        var name = funcProp.GetProperty("name").GetString() ?? "";
                        object? argsObj = null;

                        if (funcProp.TryGetProperty("arguments", out var argsProp))
                        {
                            if (argsProp.ValueKind == JsonValueKind.String)
                            {
                                var argsStr = argsProp.GetString();
                                if (!string.IsNullOrEmpty(argsStr))
                                {
                                    argsObj = JsonSerializer.Deserialize<object>(argsStr);
                                }
                            }
                            else if (argsProp.ValueKind == JsonValueKind.Object)
                            {
                                argsObj = argsProp.Clone();
                            }
                        }

                        geminiParts.Add(new
                        {
                            functionCall = new
                            {
                                name = name,
                                args = argsObj ?? new { }
                            }
                        });
                    }
                }
            }

            if (geminiParts.Count == 0)
            {
                geminiParts.Add(new { text = "" });
            }

            var geminiResponse = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = geminiParts.ToArray()
                        }
                    }
                }
            };

            return JsonSerializer.Serialize(geminiResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama copilot API call failed");
            return null;
        }
    }

    private async Task<string?> CallGeminiAsync(
        string model, string apiKey, object body, CancellationToken ct)
    {
        var provider = _config["Gemini:Provider"] ?? "Gemini";
        bool isOllama = string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase);

        if (isOllama)
        {
            try
            {
                var response = await CallOllamaChatAsync(body, ct);
                if (response != null)
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama chat failed. Gemini fallback is disabled.");
                throw;
            }
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        try
        {
            var json = JsonSerializer.Serialize(body);
            HttpResponseMessage? resp = null;
            int maxRetries = 5;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s, 8s, 16s, 32s
                    _logger.LogWarning("Gemini copilot rate limit or transient error. Retry {Attempt}/{Max} after {Delay}ms", attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs, ct);
                }

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                resp = await _http.PostAsync(url, content, ct);

                if (resp.StatusCode != System.Net.HttpStatusCode.TooManyRequests &&
                    resp.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                    break;
            }

            resp!.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini copilot API call failed");
            return null;
        }
    }
}
