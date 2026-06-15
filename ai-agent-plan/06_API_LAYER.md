# API Layer — Endpoints, DTOs, Request/Response Contracts
**OfficeTaskManagement · AiEstimationController + AgentController**

---

## AiEstimationController

**File**: `Controllers/Api/AiEstimationController.cs`  
**Route prefix**: `/api/ai`  
**Authorization**: `[Authorize]` on controller  

```csharp
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiEstimationController : ControllerBase
{
    private readonly IGeminiAiService _ai;
    private readonly ApplicationDbContext _db;
    private readonly AiEstimationLogService _log;
    private readonly IWorkflowEngineService _workflowEngine;
    
    // POST /api/ai/estimate
    // Used by: All Create/Edit forms for Project, Epic, Feature, UserStory, TaskItem
    [HttpPost("estimate")]
    public async Task<ActionResult<EstimationResult>> EstimateAsync(
        [FromBody] EstimationRequest request, CancellationToken ct)
    {
        var result = await _ai.EstimateAsync(request, ct);
        await _log.LogAsync(request.EntityType, null, 
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            result.InputTokensUsed, result.OutputTokensUsed, "gemini-2.5-flash");
        return Ok(result);
    }

    // POST /api/ai/suggest-children
    // Used by: AI panel "Suggest sub-items" — step-by-step (one level)
    [HttpPost("suggest-children")]
    public async Task<ActionResult<ChildItemSuggestions>> SuggestChildrenAsync(
        [FromBody] ChildRequest request, CancellationToken ct)
    {
        var result = await _ai.SuggestChildrenAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/full-cascade
    // Used by: "Full Breakdown" button — Epic → Features → Stories → Tasks
    [HttpPost("full-cascade")]
    public async Task<ActionResult<FullCascadeResult>> FullCascadeAsync(
        [FromBody] FullCascadeRequest request, CancellationToken ct)
    {
        var result = await _ai.GenerateFullCascadeAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/reestimate
    // Used by: "Re-estimate with AI" button on Edit pages
    [HttpPost("reestimate")]
    public async Task<ActionResult<EstimationResult>> ReEstimateAsync(
        [FromBody] ReEstimationRequest request, CancellationToken ct)
    {
        var result = await _ai.ReEstimateAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/bulk-create
    // Used by: "Create Epic + Selected Features" / "Create Story + Selected Tasks"
    // Creates all checked child items in one DB transaction
    [HttpPost("bulk-create")]
    public async Task<ActionResult<BulkCreateResult>> BulkCreateAsync(
        [FromBody] BulkCreateRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var createdIds = new List<int>();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var item in request.Items)
            {
                switch (item.EntityType)
                {
                    case "Feature":
                        var f = new Feature
                        {
                            EpicId       = item.ParentId,
                            Name         = item.Title,
                            Description  = item.Description,
                            CreatedById  = userId,
                            CreatedAt    = now.UtcDateTime,
                            TenantId     = User.FindFirstValue("TenantId") ?? ""
                        };
                        _db.Features.Add(f);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(f.Id);
                        break;

                    case "UserStory":
                        var us = new UserStory
                        {
                            FeatureId           = item.ParentId,
                            Title               = item.Title,
                            Description         = item.Description,
                            AcceptanceCriteria  = item.AcceptanceCriteria,
                            Priority            = Enum.Parse<TaskPriority>(item.Priority ?? "Medium"),
                            CreatedById         = userId,
                            CreatedAt           = now.UtcDateTime,
                            TenantId            = User.FindFirstValue("TenantId") ?? ""
                        };
                        _db.UserStories.Add(us);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(us.Id);
                        break;

                    case "Task":
                        decimal o = item.OptimisticHours ?? 0;
                        decimal m = item.MostLikelyHours ?? 0;
                        decimal p = item.PessimisticHours ?? 0;
                        decimal pert = _workflowEngine.CalculatePert(o, m, p);
                        var t = new TaskItem
                        {
                            UserStoryId              = item.ParentId,
                            Title                    = item.Title,
                            Description              = item.Description,
                            Priority                 = Enum.Parse<TaskPriority>(item.Priority ?? "Medium"),
                            EstimatedOptimisticHours = o > 0 ? o : null,
                            EstimatedMostLikelyHours = m > 0 ? m : null,
                            EstimatedPessimisticHours= p > 0 ? p : null,
                            PertEstimatedHours       = pert > 0 ? pert : null,
                            EstimatedHours           = pert > 0 ? pert : m,
                            Status                   = Models.Enums.TaskStatus.New,
                            CreatedById              = userId,
                            CreatedAt                = now.UtcDateTime,
                            TenantId                 = User.FindFirstValue("TenantId") ?? ""
                        };
                        _db.Tasks.Add(t);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(t.Id);
                        break;
                }
            }
            await tx.CommitAsync(ct);
            return Ok(new BulkCreateResult(createdIds.ToArray(), request.Items[0].EntityType));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
```

---

## BulkCreate DTOs

```csharp
public record BulkCreateRequest(
    BulkCreateItemDto[] Items
);

public record BulkCreateItemDto(
    string EntityType,       // "Feature" | "UserStory" | "Task"
    int ParentId,            // epicId / featureId / userStoryId
    string Title,
    string? Description,
    string? AcceptanceCriteria,
    string? Priority,
    decimal? OptimisticHours,
    decimal? MostLikelyHours,
    decimal? PessimisticHours
);

public record BulkCreateResult(
    int[] CreatedIds,
    string EntityType
);
```

---

## AgentController (Phase 4 — Multi-turn Copilot)

```csharp
[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agent;
    private readonly CodebaseIndexingService _indexer;

    // POST /api/agent/chat
    // Multi-turn conversation with the AI Copilot
    [HttpPost("chat")]
    public async Task<ActionResult<AgentChatResponse>> ChatAsync(
        [FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var response = await _agent.ChatAsync(request, ct);
        return Ok(response);
    }

    // DELETE /api/agent/conversation/{id}
    // Clear conversation history (reset context)
    [HttpDelete("conversation/{conversationId}")]
    public async Task<IActionResult> ClearConversationAsync(string conversationId)
    {
        await _agent.ClearConversationAsync(conversationId, default);
        return NoContent();
    }

    // POST /api/agent/reindex
    // Git webhook trigger — re-indexes codebase
    [HttpPost("reindex")]
    [AllowAnonymous]
    public IActionResult ReindexAsync(
        [FromHeader(Name = "X-Webhook-Secret")] string secret,
        [FromServices] IConfiguration config,
        [FromServices] CodebaseIndexingService indexer)
    {
        if (secret != config["Codebase:WebhookSecret"])
            return Unauthorized();
        _ = Task.Run(() => indexer.IndexRepositoryAsync(CancellationToken.None));
        return Accepted("Re-indexing started.");
    }
}
```

---

## ReEstimationRequest DTO

```csharp
public record ReEstimationRequest(
    string EntityType,
    int EntityId,
    string Title,
    string? Description,
    decimal OriginalEstimatedHours,
    decimal? ActualHoursLogged,     // From TaskHistory sum
    string? RecentCommentsSummary   // Last 3 comments summarized (≤200 chars)
);
```

---

## ChildRequest DTO

```csharp
public record ChildRequest(
    string ParentType,    // "Epic" | "Feature" | "UserStory"
    int ParentId,
    string ParentTitle,
    string? ParentDescription,
    int? ProjectId,
    bool StepByStep       // true = one level only; false = full cascade
);
```

---

## Full API Route Table

| Method | Route | Handler | Phase |
|--------|-------|---------|-------|
| POST | `/api/ai/estimate` | Estimate any entity | Phase 1 |
| POST | `/api/ai/suggest-children` | Suggest child items (step) | Phase 1 |
| POST | `/api/ai/bulk-create` | Create checked children | Phase 1 |
| POST | `/api/ai/reestimate` | Re-estimate existing item | Phase 2 |
| POST | `/api/ai/full-cascade` | Full tree breakdown | Phase 2 |
| POST | `/api/agent/chat` | Multi-turn copilot | Phase 4 |
| DELETE | `/api/agent/conversation/{id}` | Clear conversation | Phase 4 |
| POST | `/api/agent/reindex` | Trigger RAG re-index | Phase 3 |
