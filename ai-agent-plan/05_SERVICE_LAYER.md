# Service Layer Specifications
**OfficeTaskManagement · All New C# Services**

---

## IGeminiAiService + GeminiAiService

```csharp
// Services/Ai/IGeminiAiService.cs
public interface IGeminiAiService
{
    Task<EstimationResult> EstimateAsync(EstimationRequest request, CancellationToken ct = default);
    Task<ChildItemSuggestions> SuggestChildrenAsync(ChildRequest request, CancellationToken ct = default);
    Task<string> GenerateAcceptanceCriteriaAsync(string title, string description, CancellationToken ct = default);
    Task<EstimationResult> ReEstimateAsync(ReEstimationRequest request, CancellationToken ct = default);
    Task<FullCascadeResult> GenerateFullCascadeAsync(FullCascadeRequest request, CancellationToken ct = default);
}
```

### EstimationRequest DTO

```csharp
public record EstimationRequest(
    string EntityType,         // "Project" | "Epic" | "Feature" | "UserStory" | "Task"
    string Title,
    string? Description,
    int? ProjectId,
    int? EpicId,
    int? FeatureId,
    int? UserStoryId
);
```

### EstimationResult DTO

```csharp
public record EstimationResult(
    decimal OptimisticHours,
    decimal MostLikelyHours,
    decimal PessimisticHours,
    decimal PertHours,
    string Priority,
    int StoryPoints,
    decimal EstimatedBudgetBDT,
    string Confidence,        // "High" | "Medium" | "Low"
    string Rationale,
    string[] Risks,
    int InputTokensUsed,      // For cost monitoring
    int OutputTokensUsed
);
```

### ChildItemSuggestions DTO

```csharp
public record ChildItemSuggestions(
    string ParentType,
    string ChildType,
    ChildItemDto[] Items,
    string Rationale
);

public record ChildItemDto(
    string Title,
    string Description,
    decimal? OptimisticHours,
    decimal? MostLikelyHours,
    decimal? PessimisticHours,
    string Priority,
    string? AcceptanceCriteria   // Only for UserStory children
);
```

### FullCascadeResult DTO

```csharp
public record FullCascadeResult(
    CascadeFeatureDto[] Features
);

public record CascadeFeatureDto(
    string Title,
    string Description,
    CascadeUserStoryDto[] UserStories
);

public record CascadeUserStoryDto(
    string Title,
    string Description,
    string AcceptanceCriteria,
    decimal MostLikelyHours,
    CascadeTaskDto[] Tasks
);

public record CascadeTaskDto(
    string Title,
    decimal OptimisticHours,
    decimal MostLikelyHours,
    decimal PessimisticHours
);
```

---

## ContextBuilderService

```csharp
// Services/Ai/ContextBuilderService.cs
public class ContextBuilderService
{
    private readonly ApplicationDbContext _db;
    private readonly CodebaseRetrievalService _codebase;
    private readonly PmKnowledgeService _pmKnowledge;
    private readonly IMemoryCache _cache;
    private const int MaxTotalTokens = 4000;

    public async Task<PromptContext> BuildContextAsync(EstimationRequest request, CancellationToken ct = default)
    {
        int tokenBudget = MaxTotalTokens;
        var ctx = new PromptContext();

        // 1. Parent context (~400 tokens budget)
        ctx.ParentContext = await BuildParentContextAsync(request, ct);
        tokenBudget -= EstimateTokens(ctx.ParentContext);

        // 2. Sibling list — names only (~400 tokens budget)
        ctx.SiblingList = await BuildSiblingListAsync(request, ct);
        tokenBudget -= EstimateTokens(ctx.SiblingList);

        // 3. Historical accuracy stats (~500 tokens budget)
        ctx.HistoryStats = await _pmKnowledge.GetHistoryStatsAsync(
            request.ProjectId, request.EntityType, ct);
        tokenBudget -= EstimateTokens(ctx.HistoryStats);

        // 4. Hourly rate
        ctx.HourlyRateBDT = request.ProjectId.HasValue
            ? await _pmKnowledge.GetAverageHourlyRateBdtAsync(request.ProjectId.Value)
            : 800m; // fallback

        // 5. Code context — only if budget allows
        if (tokenBudget > 600)
        {
            ctx.CodeChunks = await _codebase.GetRelevantChunksAsync(
                $"{request.EntityType}: {request.Title}", topK: 3, ct: ct);
        }

        return ctx;
    }

    // Token estimation: 1 token ≈ 4 chars (Gemini approximation)
    private static int EstimateTokens(string? text)
        => string.IsNullOrEmpty(text) ? 0 : text.Length / 4;
}

public class PromptContext
{
    public string? ParentContext { get; set; }
    public string? SiblingList { get; set; }
    public string? HistoryStats { get; set; }
    public decimal HourlyRateBDT { get; set; }
    public IReadOnlyList<string>? CodeChunks { get; set; }
}
```

---

## PmKnowledgeService

```csharp
// Services/Ai/PmKnowledgeService.cs
public class PmKnowledgeService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Returns compressed historical accuracy stats for a project + entity type.
    /// Format: "Backend tasks: avg 8h est → 11h actual (38% overrun)\n..."
    /// </summary>
    public async Task<string> GetHistoryStatsAsync(
        int? projectId, string entityType, CancellationToken ct)
    {
        if (!projectId.HasValue) return string.Empty;
        
        var cacheKey = $"history-stats:{projectId}:{entityType}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        // Get tasks with both estimated and actual hours (completed tasks = reliable data)
        var doneTasks = await _db.Tasks
            .Where(t => t.ProjectId == projectId 
                && t.Status == Models.Enums.TaskStatus.Done
                && t.ActualHours.HasValue 
                && t.EstimatedHours > 0)
            .Select(t => new { t.Type, t.EstimatedHours, t.ActualHours })
            .Take(50) // Cap to avoid large queries
            .ToListAsync(ct);

        if (!doneTasks.Any()) return "No historical completion data available for this project.";

        var sb = new StringBuilder("Historical estimation accuracy for this project:\n");
        var avg = doneTasks.Average(t => 
            (double)((t.ActualHours ?? 0) - t.EstimatedHours) / (double)t.EstimatedHours * 100);
        sb.AppendLine($"- Overall: avg {avg:F0}% overrun ({doneTasks.Count} completed tasks)");

        // Most similar recent item
        var recent = doneTasks.LastOrDefault();
        if (recent != null)
            sb.AppendLine($"- Recent example: estimated {recent.EstimatedHours:F0}h, actual {recent.ActualHours:F0}h");

        // Team velocity (sprints)
        var sprints = await _db.Sprints
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.StartDate)
            .Take(6)
            .Select(s => new { s.Name, TaskCount = _db.Tasks.Count(t => t.SprintId == s.Id && t.Status == Models.Enums.TaskStatus.Done) })
            .ToListAsync(ct);
        if (sprints.Any())
            sb.AppendLine($"- Team velocity: avg {sprints.Average(s => s.TaskCount):F0} tasks/sprint (last {sprints.Count} sprints)");

        var result = sb.ToString();
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    public async Task<decimal> GetAverageHourlyRateBdtAsync(int projectId)
    {
        // See 03_PROMPT_STRATEGY.md for full implementation
        var allocatedUserIds = await _db.ProjectResourceAllocations
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync();

        if (!allocatedUserIds.Any()) return 800m;

        var rates = await _db.SalaryHistories
            .Where(s => allocatedUserIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.EffectiveDate).First().Amount)
            .ToListAsync();

        return rates.Any() ? rates.Average() / 22 / 8 : 800m;
    }
}
```

---

## AgentService (Phase 4 — Multi-turn Copilot)

```csharp
// Services/Agent/IAgentService.cs
public interface IAgentService
{
    Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken ct = default);
    Task ClearConversationAsync(string conversationId, CancellationToken ct = default);
}

public record AgentChatRequest(
    string ConversationId,
    string UserId,
    string Message,
    string? EntityType,   // Current page context
    int? EntityId         // Current page entity ID
);

public record AgentChatResponse(
    string ConversationId,
    string Message,        // Markdown formatted response
    AgentAction[]? Actions // Suggested actions (CreateEpic, CreateFeature, etc.)
);

public record AgentAction(
    string Type,           // "create_epic" | "create_feature" | etc.
    string Label,          // "Create Epic: Authentication"
    object Payload         // The data to create
);
```

### AgentToolDispatcher — Function Call Router

```csharp
// Services/Agent/AgentToolDispatcher.cs
public class AgentToolDispatcher
{
    // Gemini returns: { "name": "create_feature", "args": { "epicId": 5, "title": "Login" } }
    // This maps it to actual EF Core operations
    
    public async Task<string> DispatchAsync(string functionName, JsonElement args, string userId)
    {
        return functionName switch
        {
            "create_epic"          => await CreateEpicAsync(args, userId),
            "create_feature"       => await CreateFeatureAsync(args, userId),
            "create_user_story"    => await CreateUserStoryAsync(args, userId),
            "create_task"          => await CreateTaskAsync(args, userId),
            "query_resource_availability" => await QueryResourcesAsync(args),
            "get_sprint_capacity"  => await GetSprintCapacityAsync(args),
            "update_estimate"      => await UpdateEstimateAsync(args, userId),
            _ => $"Unknown function: {functionName}"
        };
    }
}
```

---

## GeminiEmbeddingService (New — for RAG)

```csharp
// Services/Ai/GeminiEmbeddingService.cs
public interface IGeminiEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default);
}

public class GeminiEmbeddingService : IGeminiEmbeddingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _config["Gemini:ApiKey"];
        var model  = _config["Gemini:EmbeddingModel"] ?? "models/text-embedding-004";
        var url    = $"https://generativelanguage.googleapis.com/v1beta/{model}:embedContent?key={apiKey}";
        
        var body = new { content = new { parts = new[] { new { text } } } };
        var resp = await _http.PostAsync(url, 
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        
        return doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values")
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();
    }

    public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
    {
        // Batch embedding for efficiency during indexing
        // Gemini supports up to 100 texts per batch call
        var results = new List<float[]>();
        foreach (var text in texts) // TODO: real batch API when available
            results.Add(await EmbedAsync(text, ct));
        return results.ToArray();
    }
}
```

---

## AiEstimationLogService (Cost Monitoring)

```csharp
// Services/Ai/AiEstimationLogService.cs
public class AiEstimationLogService
{
    private readonly ApplicationDbContext _db;

    public async Task LogAsync(
        string entityType, int? entityId, string userId,
        int inputTokens, int outputTokens, string model)
    {
        _db.AiEstimationLogs.Add(new AiEstimationLog
        {
            EntityType    = entityType,
            EntityId      = entityId,
            UserId        = userId,
            InputTokens   = inputTokens,
            OutputTokens  = outputTokens,
            Model         = model,
            CreatedAt     = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
```

---

## Program.cs Registrations (to add)

```csharp
// Add after existing service registrations in Program.cs:

// AI Services
builder.Services.AddHttpClient<GeminiAiService>();
builder.Services.AddScoped<IGeminiAiService, GeminiAiService>();
builder.Services.AddHttpClient<GeminiEmbeddingService>();
builder.Services.AddScoped<IGeminiEmbeddingService, GeminiEmbeddingService>();
builder.Services.AddScoped<ContextBuilderService>();
builder.Services.AddScoped<PmKnowledgeService>();
builder.Services.AddScoped<AiEstimationLogService>();

// Codebase RAG (Phase 3)
builder.Services.AddScoped<CodebaseRetrievalService>();
builder.Services.AddHostedService<CodebaseIndexingService>();

// Agent / Multi-turn Copilot (Phase 4)
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<AgentConversationService>();
builder.Services.AddScoped<AgentToolDispatcher>();
```
