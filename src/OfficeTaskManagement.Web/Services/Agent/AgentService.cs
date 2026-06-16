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

    private const int MaxFunctionCallRounds = 5; // prevent infinite agentic loops

    public AgentService(
        HttpClient http,
        IConfiguration config,
        AgentConversationService conversationService,
        AgentToolDispatcher dispatcher,
        ContextBuilderService contextBuilder,
        ILogger<AgentService> logger)
    {
        _http = http;
        _config = config;
        _conversationService = conversationService;
        _dispatcher = dispatcher;
        _contextBuilder = contextBuilder;
        _logger = logger;
    }

    // ── ChatAsync — multi-turn agentic loop ────────────────────────────────────
    public async Task<AgentChatResponse> ChatAsync(
        AgentChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _config["Gemini:ApiKey"];
        var model  = _config["Gemini:CopilotModel"] ?? "gemini-2.5-pro";

        if (string.IsNullOrEmpty(apiKey))
        {
            return new AgentChatResponse(
                request.ConversationId,
                "AI Copilot is not configured. Please add a Gemini API key in settings.",
                null);
        }

        // 1. Load conversation history
        var tenantId = ""; // will be enriched by controller
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

        while (rounds < MaxFunctionCallRounds)
        {
            rounds++;
            var responseJson = await CallGeminiAsync(model, apiKey, currentBody, ct);
            if (responseJson == null) break;

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

        finalText ??= "I processed your request. " +
                      (suggestedActions.Any()
                          ? $"{suggestedActions.Count} action(s) were completed."
                          : "How else can I help?");

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

    // ── Helpers ────────────────────────────────────────────────────────────────
    private async Task<string> BuildSystemInstructionAsync(
        AgentChatRequest request, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert AI Project Management Copilot for OfficeTaskManagement.");
        sb.AppendLine("Help the user manage their projects using PERT three-point estimation (O+4M+P)/6.");
        sb.AppendLine("Weekend is Friday+Saturday in Bangladesh. Currency is BDT.");
        sb.AppendLine("When creating items, always use the provided tools — never just describe what to do.");
        sb.AppendLine("Keep responses concise. Use markdown formatting.");

        if (!string.IsNullOrEmpty(request.EntityType) && request.EntityId.HasValue)
        {
            sb.AppendLine($"\nCurrent context: {request.EntityType} ID={request.EntityId}");
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

    private static object[] BuildContentsWithFunctionResponses(
        object[] previousContents,
        JsonElement modelParts,
        List<object> functionResponses)
    {
        var contents = previousContents.ToList();

        // Add the model turn that contained the function call
        contents.Add(new
        {
            role  = "model",
            parts = modelParts.EnumerateArray()
                              .Select(p => (object)new { text = p.TryGetProperty("text", out var t) ? t.GetString() : "" })
                              .ToArray()
        });

        // Add function responses
        contents.Add(new
        {
            role  = "user",
            parts = functionResponses.ToArray()
        });

        return contents.ToArray();
    }

    private async Task<string?> CallGeminiAsync(
        string model, string apiKey, object body, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        try
        {
            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp    = await _http.PostAsync(url, content, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini copilot API call failed");
            return null;
        }
    }
}
