using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.WorkflowEngine;

namespace OfficeTaskManagement.Services.Agent;

/// <summary>
/// Routes Gemini function_call results to the appropriate EF Core operations.
/// Each method returns a string result that is sent back to Gemini as a function response.
/// Spec: ai-agent-plan/05_SERVICE_LAYER.md → AgentToolDispatcher
/// </summary>
public class AgentToolDispatcher
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkflowEngineService _workflowEngine;
    private readonly ILogger<AgentToolDispatcher> _logger;

    public AgentToolDispatcher(
        ApplicationDbContext db,
        IWorkflowEngineService workflowEngine,
        ILogger<AgentToolDispatcher> logger)
    {
        _db = db;
        _workflowEngine = workflowEngine;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches a Gemini function call to the correct handler.
    /// Returns a string result to feed back into the Gemini conversation.
    /// </summary>
    public async Task<string> DispatchAsync(
        string functionName,
        JsonElement args,
        string userId,
        string tenantId,
        CancellationToken ct = default)
    {
        try
        {
            return functionName switch
            {
                "create_epic"                 => await CreateEpicAsync(args, userId, tenantId, ct),
                "create_feature"              => await CreateFeatureAsync(args, userId, tenantId, ct),
                "create_user_story"           => await CreateUserStoryAsync(args, userId, tenantId, ct),
                "create_task"                 => await CreateTaskAsync(args, userId, tenantId, ct),
                "query_resource_availability" => await QueryResourcesAsync(args, ct),
                "get_sprint_capacity"         => await GetSprintCapacityAsync(args, ct),
                "update_estimate"             => await UpdateEstimateAsync(args, userId, ct),
                _                             => $"Unknown function: {functionName}. No action taken."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentToolDispatcher failed for function {Name}", functionName);
            return $"Error executing {functionName}: {ex.Message}";
        }
    }

    // ── create_epic ────────────────────────────────────────────────────────────
    private async Task<string> CreateEpicAsync(
        JsonElement args, string userId, string tenantId, CancellationToken ct)
    {
        var projectId   = args.GetProperty("projectId").GetInt32();
        var name        = args.GetProperty("name").GetString() ?? "Unnamed Epic";
        var description = args.TryGetProperty("description", out var d) ? d.GetString() : null;
        var priority    = args.TryGetProperty("priority",    out var p) ? p.GetString() : "Medium";

        var epic = new Epic
        {
            ProjectId   = projectId,
            Name        = name,
            Description = description,
            CreatedById = userId,
            CreatedAt   = DateTime.UtcNow,
            TenantId    = tenantId
        };
        _db.Epics.Add(epic);
        await _db.SaveChangesAsync(ct);

        return $"Epic created successfully: ID={epic.Id}, Name=\"{name}\", ProjectId={projectId}";
    }

    // ── create_feature ─────────────────────────────────────────────────────────
    private async Task<string> CreateFeatureAsync(
        JsonElement args, string userId, string tenantId, CancellationToken ct)
    {
        var epicId      = args.GetProperty("epicId").GetInt32();
        var name        = args.GetProperty("name").GetString() ?? "Unnamed Feature";
        var description = args.TryGetProperty("description", out var d) ? d.GetString() : null;

        var feature = new Feature
        {
            EpicId      = epicId,
            Name        = name,
            Description = description,
            CreatedById = userId,
            CreatedAt   = DateTime.UtcNow,
            TenantId    = tenantId
        };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync(ct);

        return $"Feature created: ID={feature.Id}, Name=\"{name}\", EpicId={epicId}";
    }

    // ── create_user_story ──────────────────────────────────────────────────────
    private async Task<string> CreateUserStoryAsync(
        JsonElement args, string userId, string tenantId, CancellationToken ct)
    {
        var featureId          = args.GetProperty("featureId").GetInt32();
        var title              = args.GetProperty("title").GetString() ?? "Unnamed Story";
        var description        = args.TryGetProperty("description",        out var d) ? d.GetString() : null;
        var acceptanceCriteria = args.TryGetProperty("acceptanceCriteria", out var a) ? a.GetString() : null;
        var priorityStr        = args.TryGetProperty("priority",           out var p) ? p.GetString() : "Medium";
        Enum.TryParse<TaskPriority>(priorityStr, out var priority);

        var story = new UserStory
        {
            FeatureId          = featureId,
            Title              = title,
            Description        = description,
            AcceptanceCriteria = acceptanceCriteria,
            Priority           = priority,
            CreatedById        = userId,
            CreatedAt          = DateTime.UtcNow,
            TenantId           = tenantId
        };
        _db.UserStories.Add(story);
        await _db.SaveChangesAsync(ct);

        return $"User Story created: ID={story.Id}, Title=\"{title}\", FeatureId={featureId}";
    }

    // ── create_task ────────────────────────────────────────────────────────────
    private async Task<string> CreateTaskAsync(
        JsonElement args, string userId, string tenantId, CancellationToken ct)
    {
        var userStoryId = args.GetProperty("userStoryId").GetInt32();
        var title       = args.GetProperty("title").GetString() ?? "Unnamed Task";
        var description = args.TryGetProperty("description", out var d) ? d.GetString() : null;
        var priorityStr = args.TryGetProperty("priority",    out var p) ? p.GetString() : "Medium";
        Enum.TryParse<TaskPriority>(priorityStr, out var priority);

        decimal o = args.TryGetProperty("optimisticHours",  out var ov) ? (decimal)ov.GetDouble() : 0;
        decimal m = args.TryGetProperty("mostLikelyHours",  out var mv) ? (decimal)mv.GetDouble() : 0;
        decimal pe = args.TryGetProperty("pessimisticHours", out var pv) ? (decimal)pv.GetDouble() : 0;
        decimal pert = (o > 0 && m > 0 && pe > 0) ? _workflowEngine.CalculatePert(o, m, pe) : 0;

        var task = new TaskItem
        {
            UserStoryId               = userStoryId,
            Title                     = title,
            Description               = description,
            Priority                  = priority,
            EstimatedOptimisticHours  = o  > 0 ? o  : null,
            EstimatedMostLikelyHours  = m  > 0 ? m  : null,
            EstimatedPessimisticHours = pe > 0 ? pe : null,
            PertEstimatedHours        = pert > 0 ? pert : null,
            EstimatedHours            = pert > 0 ? pert : (m > 0 ? m : 0m),
            Status                    = Models.Enums.TaskStatus.New,
            CreatedById               = userId,
            CreatedAt                 = DateTime.UtcNow,
            TenantId                  = tenantId
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);

        return $"Task created: ID={task.Id}, Title=\"{title}\", PERT={pert:F1}h, UserStoryId={userStoryId}";
    }

    // ── query_resource_availability ────────────────────────────────────────────
    private async Task<string> QueryResourcesAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();
        var startStr  = args.TryGetProperty("startDate", out var s) ? s.GetString() : null;
        var endStr    = args.TryGetProperty("endDate",   out var e) ? e.GetString() : null;

        var allocations = await _db.ProjectResourceAllocations
            .Where(a => a.ProjectId == projectId)
            .Include(a => a.User)
            .AsNoTracking()
            .ToListAsync(ct);

        if (!allocations.Any())
            return $"No resources allocated to project {projectId}.";

        var sb = new System.Text.StringBuilder($"Resources on project {projectId}:\n");
        foreach (var alloc in allocations)
        {
            sb.AppendLine($"  - {alloc.User?.FullName ?? alloc.UserId}: {alloc.AllocationPercentage}% allocated");
        }
        return sb.ToString();
    }

    // ── get_sprint_capacity ────────────────────────────────────────────────────
    private async Task<string> GetSprintCapacityAsync(JsonElement args, CancellationToken ct)
    {
        var sprintId = args.GetProperty("sprintId").GetInt32();
        var sprint   = await _db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sprintId, ct);

        if (sprint == null) return $"Sprint {sprintId} not found.";

        var taskCount = await _db.Tasks.CountAsync(t => t.SprintId == sprintId, ct);
        var totalEst  = await _db.Tasks
            .Where(t => t.SprintId == sprintId)
            .SumAsync(t => (decimal?)t.EstimatedHours ?? 0, ct);

        return $"Sprint \"{sprint.Name}\": {taskCount} tasks, {totalEst:F0}h estimated total.";
    }

    // ── update_estimate ────────────────────────────────────────────────────────
    private async Task<string> UpdateEstimateAsync(JsonElement args, string userId, CancellationToken ct)
    {
        var taskId = args.GetProperty("taskId").GetInt32();
        var task   = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task == null) return $"Task {taskId} not found.";

        decimal o  = (decimal)args.GetProperty("optimisticHours").GetDouble();
        decimal m  = (decimal)args.GetProperty("mostLikelyHours").GetDouble();
        decimal p  = (decimal)args.GetProperty("pessimisticHours").GetDouble();
        decimal pert = _workflowEngine.CalculatePert(o, m, p);

        task.EstimatedOptimisticHours  = o;
        task.EstimatedMostLikelyHours  = m;
        task.EstimatedPessimisticHours = p;
        task.PertEstimatedHours        = pert;
        task.EstimatedHours            = pert;

        await _db.SaveChangesAsync(ct);
        return $"Task {taskId} estimate updated: O={o}h, M={m}h, P={p}h, PERT={pert:F1}h";
    }
}
