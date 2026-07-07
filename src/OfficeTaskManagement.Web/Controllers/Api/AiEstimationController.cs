using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.WorkflowEngine;
using System.Security.Claims;

namespace OfficeTaskManagement.Controllers.Api;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiEstimationController : ControllerBase
{
    private readonly IGeminiAiService _ai;
    private readonly ApplicationDbContext _db;
    private readonly AiEstimationLogService _log;
    private readonly IWorkflowEngineService _workflowEngine;

    public AiEstimationController(
        IGeminiAiService ai,
        ApplicationDbContext db,
        AiEstimationLogService log,
        IWorkflowEngineService workflowEngine)
    {
        _ai = ai;
        _db = db;
        _log = log;
        _workflowEngine = workflowEngine;
    }

    // POST /api/ai/estimate
    [HttpPost("estimate")]
    public async Task<ActionResult<EstimationResult>> EstimateAsync(
        [FromBody] EstimationRequest request, CancellationToken ct)
    {
        var result = await _ai.EstimateAsync(request, ct);
        var tenantId = _db.CurrentTenantId;
        await _log.LogAsync(
            request.EntityType,
            null,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            tenantId,
            result.InputTokensUsed,
            result.OutputTokensUsed,
            "gemini-2.5-flash");
        return Ok(result);
    }

    // POST /api/ai/suggest-children
    [HttpPost("suggest-children")]
    public async Task<ActionResult<ChildItemSuggestions>> SuggestChildrenAsync(
        [FromBody] ChildRequest request, CancellationToken ct)
    {
        var result = await _ai.SuggestChildrenAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/full-cascade
    [HttpPost("full-cascade")]
    public async Task<ActionResult<FullCascadeResult>> FullCascadeAsync(
        [FromBody] FullCascadeRequest request, CancellationToken ct)
    {
        var result = await _ai.GenerateFullCascadeAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/reestimate
    [HttpPost("reestimate")]
    public async Task<ActionResult<EstimationResult>> ReEstimateAsync(
        [FromBody] ReEstimationRequest request, CancellationToken ct)
    {
        var result = await _ai.ReEstimateAsync(request, ct);
        return Ok(result);
    }

    // POST /api/ai/bulk-create
    [HttpPost("bulk-create")]
    public async Task<ActionResult<BulkCreateResult>> BulkCreateAsync(
        [FromBody] BulkCreateRequest request, CancellationToken ct)
    {
        if (request.Items == null || request.Items.Length == 0)
            return BadRequest("No items to create.");

        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var tenantId = _db.CurrentTenantId;
        var now      = DateTimeOffset.UtcNow.UtcDateTime;
        var createdIds = new List<int>();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var item in request.Items)
            {
                switch (item.EntityType)
                {
                    case "Epic":
                    {
                        var e = new Epic
                        {
                            ProjectId   = item.ParentId,
                            Name        = item.Title,
                            Description = item.Description,
                            CreatedById = userId,
                            CreatedAt   = now,
                            TenantId    = tenantId
                        };
                        _db.Epics.Add(e);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(e.Id);
                        break;
                    }
                    case "Feature":
                    {
                        var f = new Feature
                        {
                            EpicId      = item.ParentId,
                            Name        = item.Title,
                            Description = item.Description,
                            CreatedById = userId,
                            CreatedAt   = now,
                            TenantId    = tenantId
                        };
                        _db.Features.Add(f);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(f.Id);
                        break;
                    }
                    case "UserStory":
                    {
                        var us = new UserStory
                        {
                            FeatureId          = item.ParentId,
                            Title              = item.Title,
                            Description        = item.Description,
                            AcceptanceCriteria = item.AcceptanceCriteria,
                            Priority           = Enum.TryParse<TaskPriority>(item.Priority, out var usPrio)
                                                 ? usPrio : TaskPriority.Medium,
                            CreatedById        = userId,
                            CreatedAt          = now,
                            TenantId           = tenantId
                        };
                        _db.UserStories.Add(us);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(us.Id);
                        break;
                    }
                    case "Task":
                    {
                        decimal o    = item.OptimisticHours  ?? 0;
                        decimal m    = item.MostLikelyHours  ?? 0;
                        decimal p    = item.PessimisticHours ?? 0;
                        decimal pert = _workflowEngine.CalculatePert(o, m, p);
                        var t = new TaskItem
                        {
                            UserStoryId               = item.ParentId,
                            Title                     = item.Title,
                            Description               = item.Description,
                            Priority                  = Enum.TryParse<TaskPriority>(item.Priority, out var tPrio)
                                                        ? tPrio : TaskPriority.Medium,
                            EstimatedOptimisticHours  = o > 0 ? o : null,
                            EstimatedMostLikelyHours  = m > 0 ? m : null,
                            EstimatedPessimisticHours = p > 0 ? p : null,
                            PertEstimatedHours        = pert > 0 ? pert : null,
                            EstimatedHours            = pert > 0 ? pert : (m > 0 ? m : 0m),
                            Status                    = Models.Enums.TaskStatus.New,
                            CreatedById               = userId,
                            CreatedAt                 = now,
                            TenantId                  = tenantId
                        };
                        _db.Tasks.Add(t);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(t.Id);
                        break;
                    }
                }
            }
            await tx.CommitAsync(ct);
            return Ok(new BulkCreateResult(
                createdIds.ToArray(),
                request.Items[0].EntityType,
                $"{createdIds.Count} {request.Items[0].EntityType}(s) created successfully."));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // POST /api/ai/create-wbs
    [HttpPost("create-wbs")]
    public async Task<ActionResult<BulkCreateResult>> CreateWbsAsync(
        [FromBody] CreateWbsRequest request, CancellationToken ct)
    {
        if (request == null || request.Wbs == null || request.Wbs.Count == 0)
            return BadRequest("No WBS items provided.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var tenantId = _db.CurrentTenantId;
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var createdIds = new List<int>();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            int epicCount = 0, featureCount = 0, storyCount = 0, taskCount = 0;

            foreach (var epicDto in request.Wbs)
            {
                var epic = new Epic
                {
                    ProjectId = request.ProjectId,
                    Name = epicDto.Name,
                    Description = epicDto.Description,
                    CreatedById = userId,
                    CreatedAt = now,
                    TenantId = tenantId
                };
                _db.Epics.Add(epic);
                await _db.SaveChangesAsync(ct);
                createdIds.Add(epic.Id);
                epicCount++;

                if (epicDto.Features == null) continue;
                foreach (var featDto in epicDto.Features)
                {
                    var feature = new Feature
                    {
                        EpicId = epic.Id,
                        Name = featDto.Name,
                        Description = featDto.Description,
                        CreatedById = userId,
                        CreatedAt = now,
                        TenantId = tenantId
                    };
                    _db.Features.Add(feature);
                    await _db.SaveChangesAsync(ct);
                    createdIds.Add(feature.Id);
                    featureCount++;

                    if (featDto.Stories == null) continue;
                    foreach (var storyDto in featDto.Stories)
                    {
                        var story = new UserStory
                        {
                            FeatureId = feature.Id,
                            Title = storyDto.Title,
                            Description = storyDto.Description,
                            AcceptanceCriteria = storyDto.AcceptanceCriteria,
                            Priority = TaskPriority.Medium,
                            CreatedById = userId,
                            CreatedAt = now,
                            TenantId = tenantId
                        };
                        _db.UserStories.Add(story);
                        await _db.SaveChangesAsync(ct);
                        createdIds.Add(story.Id);
                        storyCount++;

                        if (storyDto.Tasks == null) continue;
                        foreach (var taskDto in storyDto.Tasks)
                        {
                            decimal o = taskDto.OptimisticHours;
                            decimal m = taskDto.MostLikelyHours;
                            decimal p = taskDto.PessimisticHours;
                            decimal pert = (o > 0 && m > 0 && p > 0) ? _workflowEngine.CalculatePert(o, m, p) : 0;

                            var task = new TaskItem
                            {
                                UserStoryId = story.Id,
                                ProjectId = request.ProjectId,
                                Title = taskDto.Title,
                                Description = taskDto.Description,
                                Priority = TaskPriority.Medium,
                                EstimatedOptimisticHours = o > 0 ? o : null,
                                EstimatedMostLikelyHours = m > 0 ? m : null,
                                EstimatedPessimisticHours = p > 0 ? p : null,
                                PertEstimatedHours = pert > 0 ? pert : null,
                                EstimatedHours = pert > 0 ? pert : (m > 0 ? m : 0m),
                                Status = Models.Enums.TaskStatus.New,
                                CreatedById = userId,
                                CreatedAt = now,
                                TenantId = tenantId
                            };
                            _db.Tasks.Add(task);
                            taskCount++;
                        }
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }

            await tx.CommitAsync(ct);
            return Ok(new BulkCreateResult(
                createdIds.ToArray(),
                "Epic",
                $"Successfully created {epicCount} epic(s), {featureCount} feature(s), {storyCount} user stor(ies), and {taskCount} task(s)."));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // POST /api/ai/bulk-reestimate  (T54)
    // Re-estimates multiple tasks and returns updated PERT for each.
    [HttpPost("bulk-reestimate")]
    public async Task<ActionResult<IEnumerable<object>>> BulkReEstimateAsync(
        [FromBody] int[] taskIds, CancellationToken ct)
    {
        if (taskIds == null || taskIds.Length == 0)
            return BadRequest("No task IDs provided.");

        var tenantId = _db.CurrentTenantId;
        var tasks = await _db.Tasks
            .Where(t => taskIds.Contains(t.Id) && t.TenantId == tenantId)
            .ToListAsync(ct);

        var results = new List<object>();
        foreach (var task in tasks)
        {
            var req = new ReEstimationRequest(
                EntityType: "Task",
                Title: task.Title,
                Description: task.Description,
                EntityId: task.Id,
                ProjectId: null,
                OriginalPertHours: task.PertEstimatedHours ?? task.EstimatedHours,
                ActualHoursLogged: task.ActualHours,
                ChangeReason: "Bulk re-estimation"
            );

            var result = await _ai.ReEstimateAsync(req, ct);
            results.Add(new
            {
                taskId         = task.Id,
                title          = task.Title,
                originalHours  = task.EstimatedHours,
                newPertHours   = result.PertHours,
                confidence     = result.Confidence,
                rationale      = result.Rationale
            });
        }
        return Ok(results);
    }

    // GET /api/ai/usage-stats  (T55) — Admin only
    [HttpGet("usage-stats")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<IEnumerable<object>>> UsageStatsAsync(CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;

        // Materialize in memory to avoid EF Core GroupBy translation limitations
        var rawLogs = await _db.AiEstimationLogs
            .Where(l => l.TenantId == tenantId)
            .Select(l => new { l.Model, l.CreatedAt, l.InputTokens, l.OutputTokens })
            .ToListAsync(ct);

        var stats = rawLogs
            .GroupBy(l => new { l.Model, l.CreatedAt.Month, l.CreatedAt.Year })
            .Select(g => new
            {
                model        = g.Key.Model,
                year         = g.Key.Year,
                month        = g.Key.Month,
                totalCalls   = g.Count(),
                inputTokens  = g.Sum(l => l.InputTokens),
                outputTokens = g.Sum(l => l.OutputTokens)
            })
            .OrderByDescending(x => x.year).ThenByDescending(x => x.month)
            .ToList();

        return Ok(stats);
    }
}

