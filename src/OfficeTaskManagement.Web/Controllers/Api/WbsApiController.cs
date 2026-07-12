using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.WorkflowEngine;
using System.Security.Claims;

namespace OfficeTaskManagement.Controllers.Api;

[ApiController]
[Route("api/wbs")]
[Authorize]
public class WbsApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkflowEngineService _workflowEngine;

    public WbsApiController(ApplicationDbContext db, IWorkflowEngineService workflowEngine)
    {
        _db = db;
        _workflowEngine = workflowEngine;
    }

    [HttpPost("bulk-create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCreateWbsAsync([FromBody] JsonElement args, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var tenantId = _db.CurrentTenantId;
        
        var projectId = args.GetProperty("projectId").GetInt32();
        var wbsArray  = args.GetProperty("wbs");

        int epicCount = 0, featureCount = 0, storyCount = 0, taskCount = 0, testCaseCount = 0;

        foreach (var epicEl in wbsArray.EnumerateArray())
        {
            var epicName = epicEl.TryGetProperty("name", out var en) ? en.GetString() ?? "Unnamed Epic" : "Unnamed Epic";
            var epicDesc = epicEl.TryGetProperty("description", out var ed) ? ed.GetString() : null;

            var targetProjectId = projectId;
            if (epicEl.TryGetProperty("projectId", out var epicProjProp) && epicProjProp.ValueKind == JsonValueKind.Number)
            {
                targetProjectId = epicProjProp.GetInt32();
            }

            // Check if there is an existing Epic with this ID or name in the project
            Epic? epic = null;
            if (epicEl.TryGetProperty("id", out var epicIdProp) && epicIdProp.ValueKind == JsonValueKind.Number)
            {
                var epicId = epicIdProp.GetInt32();
                epic = await _db.Epics.FirstOrDefaultAsync(e => e.Id == epicId && e.TenantId == tenantId, ct);
            }
            if (epic == null)
            {
                epic = await _db.Epics
                    .FirstOrDefaultAsync(e => e.ProjectId == targetProjectId && e.Name.ToLower() == epicName.ToLower() && e.TenantId == tenantId, ct);
            }

            if (epic == null)
            {
                epic = new Epic
                {
                    ProjectId   = targetProjectId,
                    Name        = epicName,
                    Description = epicDesc,
                    CreatedById = userId,
                    CreatedAt   = DateTime.UtcNow,
                    TenantId    = tenantId
                };
                _db.Epics.Add(epic);
                await _db.SaveChangesAsync(ct); // flush to get epic.Id
                epicCount++;
            }
            else
            {
                if (epic.Description != epicDesc || epic.ProjectId != targetProjectId || epic.Name != epicName)
                {
                    epic.Description = epicDesc;
                    epic.ProjectId = targetProjectId;
                    epic.Name = epicName;
                    _db.Epics.Update(epic);
                    await _db.SaveChangesAsync(ct);
                }
            }

            if (!epicEl.TryGetProperty("features", out var featuresEl) ||
                featuresEl.ValueKind != JsonValueKind.Array) continue;

            foreach (var featEl in featuresEl.EnumerateArray())
            {
                var featName = featEl.TryGetProperty("name", out var fn) ? fn.GetString() ?? "Unnamed Feature" : "Unnamed Feature";
                var featDesc = featEl.TryGetProperty("description", out var fd) ? fd.GetString() : null;

                // Check if there is an existing Feature with this ID or name under this Epic
                Feature? feature = null;
                if (featEl.TryGetProperty("id", out var featIdProp) && featIdProp.ValueKind == JsonValueKind.Number)
                {
                    var featId = featIdProp.GetInt32();
                    feature = await _db.Features.FirstOrDefaultAsync(f => f.Id == featId && f.TenantId == tenantId, ct);
                }
                if (feature == null)
                {
                    feature = await _db.Features
                        .FirstOrDefaultAsync(f => f.EpicId == epic.Id && f.Name.ToLower() == featName.ToLower() && f.TenantId == tenantId, ct);
                }

                if (feature == null)
                {
                    feature = new Feature
                    {
                        EpicId      = epic.Id,
                        Name        = featName,
                        Description = featDesc,
                        CreatedById = userId,
                        CreatedAt   = DateTime.UtcNow,
                        TenantId    = tenantId
                    };
                    _db.Features.Add(feature);
                    await _db.SaveChangesAsync(ct);
                    featureCount++;
                }
                else
                {
                    if (feature.Description != featDesc || feature.EpicId != epic.Id || feature.Name != featName)
                    {
                        feature.Description = featDesc;
                        feature.EpicId = epic.Id;
                        feature.Name = featName;
                        _db.Features.Update(feature);
                        await _db.SaveChangesAsync(ct);
                    }
                }

                if (!featEl.TryGetProperty("stories", out var storiesEl) ||
                    storiesEl.ValueKind != JsonValueKind.Array) continue;

                foreach (var storyEl in storiesEl.EnumerateArray())
                {
                    var title = storyEl.TryGetProperty("title", out var st) ? st.GetString() ?? "Unnamed Story" : "Unnamed Story";
                    var desc  = storyEl.TryGetProperty("description", out var sd) ? sd.GetString() : null;
                    var ac    = storyEl.TryGetProperty("acceptanceCriteria", out var acd) ? acd.GetString() : null;

                    // Check if there is an existing UserStory with this ID or title under this Feature
                    UserStory? story = null;
                    if (storyEl.TryGetProperty("id", out var storyIdProp) && storyIdProp.ValueKind == JsonValueKind.Number)
                    {
                        var storyId = storyIdProp.GetInt32();
                        story = await _db.UserStories.FirstOrDefaultAsync(us => us.Id == storyId && us.TenantId == tenantId, ct);
                    }
                    if (story == null)
                    {
                        story = await _db.UserStories
                            .FirstOrDefaultAsync(us => us.FeatureId == feature.Id && us.Title.ToLower() == title.ToLower() && us.TenantId == tenantId, ct);
                    }

                    if (story == null)
                    {
                        story = new UserStory
                        {
                            FeatureId          = feature.Id,
                            Title              = title,
                            Description        = desc,
                            AcceptanceCriteria = ac,
                            CreatedById        = userId,
                            CreatedAt          = DateTime.UtcNow,
                            TenantId           = tenantId
                        };
                        _db.UserStories.Add(story);
                        await _db.SaveChangesAsync(ct);
                        storyCount++;
                    }
                    else
                    {
                        if (story.Description != desc || story.AcceptanceCriteria != ac || story.FeatureId != feature.Id || story.Title != title)
                        {
                            story.Description = desc;
                            story.AcceptanceCriteria = ac;
                            story.FeatureId = feature.Id;
                            story.Title = title;
                            _db.UserStories.Update(story);
                            await _db.SaveChangesAsync(ct);
                        }
                    }

                    if (storyEl.TryGetProperty("testCases", out var testCasesEl) && testCasesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tcEl in testCasesEl.EnumerateArray())
                        {
                            var tcTitle    = tcEl.TryGetProperty("title", out var tct) ? tct.GetString() ?? "Unnamed Test" : "Unnamed Test";
                            var tcSteps    = tcEl.TryGetProperty("steps", out var tcs) ? tcs.GetString() ?? "" : "";
                            var tcExpected = tcEl.TryGetProperty("expectedResult", out var tce) ? tce.GetString() ?? "" : "";
                            var tcAuto     = tcEl.TryGetProperty("isAutomated", out var tca) && tca.ValueKind == JsonValueKind.True;

                            var testCase = new TestCase
                            {
                                UserStoryId    = story.Id,
                                Title          = tcTitle,
                                Steps          = tcSteps,
                                ExpectedResult = tcExpected,
                                IsAutomated    = tcAuto,
                                TenantId       = tenantId
                            };
                            _db.TestCases.Add(testCase);
                            testCaseCount++;
                        }
                    }

                    if (!storyEl.TryGetProperty("tasks", out var tasksEl) ||
                        tasksEl.ValueKind != JsonValueKind.Array) continue;

                    foreach (var taskEl in tasksEl.EnumerateArray())
                    {
                        var taskTitle = taskEl.TryGetProperty("title",       out var tt) ? tt.GetString() ?? "Unnamed Task" : "Unnamed Task";
                        var taskDesc  = taskEl.TryGetProperty("description", out var tde) ? tde.GetString() : null;

                        decimal o  = taskEl.TryGetProperty("optimisticHours",  out var ov)  ? (decimal)ov.GetDouble()  : 0;
                        decimal m  = taskEl.TryGetProperty("mostLikelyHours",  out var mv)  ? (decimal)mv.GetDouble()  : 0;
                        decimal pe = taskEl.TryGetProperty("pessimisticHours", out var pv)  ? (decimal)pv.GetDouble()  : 0;
                        decimal pert = (o > 0 && m > 0 && pe > 0) ? _workflowEngine.CalculatePert(o, m, pe) : 0;

                        var taskItem = new TaskItem
                        {
                            UserStoryId               = story.Id,
                            ProjectId                 = projectId,
                            Title                     = taskTitle,
                            Description               = taskDesc,
                            EstimatedOptimisticHours  = o  > 0 ? o  : null,
                            EstimatedMostLikelyHours  = m  > 0 ? m  : null,
                            EstimatedPessimisticHours = pe > 0 ? pe : null,
                            PertEstimatedHours        = pert > 0 ? pert : null,
                            EstimatedHours            = pert > 0 ? pert : (m > 0 ? m : 0m),
                            Status                    = OfficeTaskManagement.Models.Enums.TaskStatus.New,
                            CreatedById               = userId,
                            CreatedAt                 = DateTime.UtcNow,
                            TenantId                  = tenantId
                        };
                        _db.Tasks.Add(taskItem);
                        taskCount++;
                    }
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        return Ok($"WBS created for project {projectId}: {epicCount} epic(s), {featureCount} feature(s), {storyCount} user stor(ies), {taskCount} task(s), {testCaseCount} test case(s).");
    }
}
