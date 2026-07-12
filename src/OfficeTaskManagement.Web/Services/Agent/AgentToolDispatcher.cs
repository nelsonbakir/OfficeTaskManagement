using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.Ai;
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
    private readonly PmReportService _pmReport;
    private readonly ILogger<AgentToolDispatcher> _logger;

    public AgentToolDispatcher(
        ApplicationDbContext db,
        IWorkflowEngineService workflowEngine,
        PmReportService pmReport,
        ILogger<AgentToolDispatcher> logger)
    {
        _db             = db;
        _workflowEngine = workflowEngine;
        _pmReport       = pmReport;
        _logger         = logger;
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
                // KF-2 Read tools
                "read_project_tasks"          => await ReadProjectTasksAsync(args, ct),
                "read_sprint_list"            => await ReadSprintListAsync(args, ct),
                "read_project_status"         => await ReadProjectStatusAsync(args, ct),
                "read_existing_wbs"           => await ReadExistingWbsAsync(args, ct),
                // KF-2 Write tools
                "create_project"              => await CreateProjectAsync(args, userId, tenantId, ct),
                "assign_task"                 => await AssignTaskAsync(args, ct),
                "draft_epics"                 => await DraftEpicsAsync(args, ct),
                "draft_features"              => await DraftFeaturesAsync(args, ct),
                "draft_stories_and_tasks"     => await DraftStoriesAndTasksAsync(args, ct),
                "get_work_package_summary"    => await GetWorkPackageSummaryAsync(args, ct),
                // KF-5: PM Status Report
                "generate_status_report"      => await GenerateStatusReportAsync(args, ct),
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

        // Optional: assignee and sprint from AI args (KF-2)
        if (args.TryGetProperty("assigneeId", out var aidProp) && !string.IsNullOrEmpty(aidProp.GetString()))
            task.AssigneeId = aidProp.GetString();
        if (args.TryGetProperty("sprintId",   out var sidProp) && sidProp.ValueKind == JsonValueKind.Number)
            task.SprintId = sidProp.GetInt32();

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

    // ── read_project_tasks ─────────────────────────────────────────────────────
    private async Task<string> ReadProjectTasksAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();
        int? sprintId  = args.TryGetProperty("sprintId",   out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetInt32()    : null;
        string? status = args.TryGetProperty("status",     out var stv) ? stv.GetString() : null;
        string? assigneeId = args.TryGetProperty("assigneeId", out var av) ? av.GetString() : null;
        int limit      = args.TryGetProperty("limit",      out var lv)  && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 20;

        var query = _db.Tasks
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Assignee)
            .AsNoTracking()
            .AsQueryable();

        if (sprintId.HasValue)
            query = query.Where(t => t.SprintId == sprintId.Value);

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<Models.Enums.TaskStatus>(status, out var parsedStatus))
            query = query.Where(t => t.Status == parsedStatus);

        if (!string.IsNullOrEmpty(assigneeId))
            query = query.Where(t => t.AssigneeId == assigneeId);

        var tasks = await query.Take(limit).ToListAsync(ct);

        if (!tasks.Any())
            return $"No tasks found for project {projectId} with the given filters.";

        var sb = new System.Text.StringBuilder($"Project {projectId} tasks ({tasks.Count} returned):\n");
        foreach (var t in tasks)
        {
            var assigneeName = t.Assignee?.FullName ?? t.AssigneeId ?? "Unassigned";
            sb.AppendLine($"  - Task #{t.Id}: \"{t.Title}\" — Status: {t.Status}, Assignee: {assigneeName}, Est: {t.EstimatedHours:F0}h");
        }
        return sb.ToString();
    }

    // ── read_sprint_list ──────────────────────────────────────────────────────
    private async Task<string> ReadSprintListAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();

        var sprints = await _db.Sprints
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.StartDate)
            .AsNoTracking()
            .ToListAsync(ct);

        if (!sprints.Any())
            return $"No sprints found for project {projectId}.";

        var sb = new System.Text.StringBuilder($"Sprints for project {projectId}:\n");
        foreach (var sprint in sprints)
        {
            var total = await _db.Tasks.CountAsync(t => t.SprintId == sprint.Id, ct);
            var done  = await _db.Tasks.CountAsync(
                t => t.SprintId == sprint.Id && t.Status == Models.Enums.TaskStatus.Done, ct);
            var active = sprint.IsActive ? " [ACTIVE]" : "";
            sb.AppendLine($"  - Sprint #{sprint.Id}: \"{sprint.Name}\"{active} | {sprint.StartDate:yyyy-MM-dd} → {sprint.EndDate:yyyy-MM-dd} | {done}/{total} tasks done");
        }
        return sb.ToString();
    }

    // ── read_project_status ────────────────────────────────────────────────────
    private async Task<string> ReadProjectStatusAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return $"Project {projectId} not found.";

        var epicCount    = await _db.Epics.CountAsync(e => e.ProjectId == projectId, ct);
        var featureCount = await _db.Features.CountAsync(
            f => _db.Epics.Where(e => e.ProjectId == projectId).Select(e => e.Id).Contains(f.EpicId), ct);
        var storyCount   = await _db.UserStories.CountAsync(
            s => _db.Features
                .Where(f => _db.Epics.Where(e => e.ProjectId == projectId).Select(e => e.Id).Contains(f.EpicId))
                .Select(f => f.Id).Contains(s.FeatureId), ct);

        var tasksByStatus = await _db.Tasks
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        var totalEst    = await _db.Tasks.Where(t => t.ProjectId == projectId)
                            .SumAsync(t => (decimal?)t.EstimatedHours ?? 0, ct);
        var totalActual = await _db.Tasks.Where(t => t.ProjectId == projectId)
                            .SumAsync(t => (decimal?)t.ActualHours ?? 0, ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Project Health Snapshot: \"{project.Name}\" (ID={projectId})");
        sb.AppendLine($"Strategic Status: {project.StrategicStatus}");
        sb.AppendLine($"Epics: {epicCount} | Features: {featureCount} | User Stories: {storyCount}");
        sb.AppendLine("Task counts by status:");
        foreach (var g in tasksByStatus.OrderBy(g => g.Status))
            sb.AppendLine($"  {g.Status}: {g.Count}");
        sb.AppendLine($"Total Estimated: {totalEst:F0}h | Total Actual: {totalActual:F0}h");
        if (project.ApprovedBudget.HasValue)
            sb.AppendLine($"Approved Budget: {project.ApprovedBudget:N0} BDT");

        return sb.ToString();
    }

    // ── read_existing_wbs ──────────────────────────────────────────────────────
    private async Task<string> ReadExistingWbsAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return $"Project {projectId} not found.";

        var epics = await _db.Epics
            .Where(e => e.ProjectId == projectId)
            .Include(e => e.Features)
            .OrderBy(e => e.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        if (!epics.Any())
            return $"Project \"{project.Name}\" currently has no Epics or WBS elements. You can draft new ones from scratch.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Existing WBS structure for project \"{project.Name}\" (ID={projectId}):");
        foreach (var epic in epics)
        {
            sb.AppendLine($"- Epic: \"{epic.Name}\" (ID={epic.Id})");
            if (epic.Features != null && epic.Features.Any())
            {
                foreach (var feature in epic.Features.OrderBy(f => f.Name))
                {
                    sb.AppendLine($"  - Feature: \"{feature.Name}\" (ID={feature.Id})");
                }
            }
            else
            {
                sb.AppendLine("  - (No Features)");
            }
        }

        return sb.ToString();
    }

    // ── create_project ──────────────────────────────────────────────────────────
    private async Task<string> CreateProjectAsync(
        JsonElement args, string userId, string tenantId, CancellationToken ct)
    {
        var name        = args.GetProperty("name").GetString() ?? "Unnamed Project";
        var description = args.TryGetProperty("description", out var d)  ? d.GetString()  : null;
        var startStr    = args.TryGetProperty("startDate",   out var sd) ? sd.GetString() : null;
        var endStr      = args.TryGetProperty("endDate",     out var ed) ? ed.GetString() : null;

        DateTime? startDate = DateTime.TryParse(startStr, out var sdt) ? sdt : null;
        DateTime? endDate   = DateTime.TryParse(endStr,   out var edt) ? edt : null;

        // Project model has no StartDate/EndDate; we store them in the description
        // if present and note the request. The model fields that exist are captured below.
        var descNote = description ?? string.Empty;
        if (startDate.HasValue) descNote += $" | Planned start: {startDate.Value:yyyy-MM-dd}";
        if (endDate.HasValue)   descNote += $" | Planned end: {endDate.Value:yyyy-MM-dd}";

        var project = new Project
        {
            Name        = name,
            Description = string.IsNullOrEmpty(descNote) ? null : descNote.Trim(' ', '|', ' '),
            CreatedById = userId,
            TenantId    = tenantId,
            CreatedAt   = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        return $"Project created: ID={project.Id}, Name=\"{name}\".";
    }

    // ── wbs_drafting ──────────────────────────────────────────────────────────
    private Task<string> DraftEpicsAsync(JsonElement args, CancellationToken ct) 
        => Task.FromResult("Drafted Epics successfully. Please ask the user to review the WBS Drafting UI.");

    private Task<string> DraftFeaturesAsync(JsonElement args, CancellationToken ct) 
        => Task.FromResult("Drafted Features successfully. Please ask the user to review the WBS Drafting UI.");

    private Task<string> DraftStoriesAndTasksAsync(JsonElement args, CancellationToken ct) 
        => Task.FromResult("Drafted Stories and Tasks successfully. Please ask the user to review the WBS Drafting UI.");

    // ── assign_task ──────────────────────────────────────────────────────────────
    private async Task<string> AssignTaskAsync(JsonElement args, CancellationToken ct)
    {
        var taskId = args.GetProperty("taskId").GetInt32();
        var task   = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return $"Task {taskId} not found.";

        bool changed = false;
        if (args.TryGetProperty("assigneeUserId", out var aProp) &&
            !string.IsNullOrEmpty(aProp.GetString()))
        {
            task.AssigneeId = aProp.GetString();
            changed = true;
        }
        if (args.TryGetProperty("sprintId", out var sProp) &&
            sProp.ValueKind == JsonValueKind.Number)
        {
            task.SprintId = sProp.GetInt32();
            changed = true;
        }

        if (!changed)
            return $"Task {taskId}: no assignee or sprint changes specified.";

        await _db.SaveChangesAsync(ct);
        return $"Task {taskId} updated — AssigneeId: {task.AssigneeId ?? "(unchanged)"}, SprintId: {task.SprintId?.ToString() ?? "(unchanged)"}";
    }

    // ── get_work_package_summary ────────────────────────────────────────────────
    private async Task<string> GetWorkPackageSummaryAsync(JsonElement args, CancellationToken ct)
    {
        var taskId  = args.GetProperty("taskId").GetInt32();
        var summary = await _workflowEngine.GetWorkPackageSummaryAsync(taskId);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Work Package Summary: \"{summary.ParentTaskTitle}\" (Task #{summary.ParentTaskId})");
        sb.AppendLine($"Total PERT Estimate: {summary.TotalPertEstimatedHours:F1}h");
        sb.AppendLine($"Total Actual Hours:  {summary.TotalActualHours:F1}h");
        sb.AppendLine($"Effort Variance:     {summary.EffortVarianceHours:+0.0;-0.0}h ({summary.EffortVariancePercent:+0.0;-0.0}%)");

        if (summary.Stages.Count > 0)
        {
            sb.AppendLine("\n### Stage Breakdown");
            foreach (var stage in summary.Stages)
            {
                sb.AppendLine($"  [{stage.StageOrder}] {stage.StageName} ({stage.DefaultRoleTitle})");
                sb.AppendLine($"      Assignee: {stage.AssigneeName ?? "(unassigned)"} | Status: {stage.Status}");
                sb.AppendLine($"      PERT: {stage.PertHours:F1}h | Actual: {stage.ActualHours:F1}h | Time-in-status: {stage.TimeInStatusHours:F1}h");
            }
        }

        return sb.ToString();
    }

    // ── generate_status_report ──────────────────────────────────────────────────
    private async Task<string> GenerateStatusReportAsync(JsonElement args, CancellationToken ct)
    {
        var projectId = args.GetProperty("projectId").GetInt32();
        return await _pmReport.GenerateMarkdownReportAsync(projectId, ct);
    }
}
