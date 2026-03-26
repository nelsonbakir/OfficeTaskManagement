using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;

using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/strategic")]
    [ApiController]
    [Authorize(Roles = "Manager")]
    public class StrategicDecisionApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public StrategicDecisionApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        private async Task LogDecision(string type, int? projectId, int? taskId, string? notes, string? metadata = null)
        {
            _context.PortfolioDecisions.Add(new PortfolioDecision
            {
                DecisionType = type,
                ProjectId = projectId,
                TaskId = taskId,
                Notes = notes,
                Metadata = metadata,
                MadeById = CurrentUserId,
                MadeAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ── PROJECT CONTROLS ────────────────────────────────────────────────

        /// <summary>PUT a project on hold — pauses all its active In-Progress tasks.</summary>
        [HttpPost("project/{id}/hold")]
        public async Task<IActionResult> HoldProject(int id, [FromBody] DecisionRequest req)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.StrategicStatus = ProjectStrategicStatus.OnHold;
            project.StrategicStatusReason = req.Notes;
            project.StrategicStatusChangedAt = DateTime.UtcNow;
            project.StrategicStatusChangedById = CurrentUserId;

            // Soft-pause all active tasks on this project
            var activeTasks = await _context.Tasks
                .Where(t => t.ProjectId == id && t.Status != TaskStatus.Done && !t.IsPaused)
                .ToListAsync();

            foreach (var task in activeTasks)
            {
                task.IsPaused = true;
                task.PauseReason = $"Project put on hold: {req.Notes}";
                task.PausedAt = DateTime.UtcNow;
                task.PausedById = CurrentUserId;
            }

            await _context.SaveChangesAsync();
            await LogDecision("PauseProject", id, null, req.Notes,
                $"{{\"tasksAffected\":{activeTasks.Count}}}");

            return Ok(new { success = true, message = $"Project '{project.Name}' placed on hold. {activeTasks.Count} task(s) paused." });
        }

        /// <summary>Resume a project that was on hold.</summary>
        [HttpPost("project/{id}/resume")]
        public async Task<IActionResult> ResumeProject(int id, [FromBody] DecisionRequest req)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.StrategicStatus = ProjectStrategicStatus.Active;
            project.StrategicStatusReason = req.Notes;
            project.StrategicStatusChangedAt = DateTime.UtcNow;
            project.StrategicStatusChangedById = CurrentUserId;

            // Resume all paused tasks belonging to this project
            var pausedTasks = await _context.Tasks
                .Where(t => t.ProjectId == id && t.IsPaused)
                .ToListAsync();

            foreach (var task in pausedTasks)
            {
                task.IsPaused = false;
                task.PauseReason = null;
                task.PausedAt = null;
                task.PausedById = null;
            }

            await _context.SaveChangesAsync();
            await LogDecision("ResumeProject", id, null, req.Notes,
                $"{{\"tasksResumed\":{pausedTasks.Count}}}");

            return Ok(new { success = true, message = $"Project '{project.Name}' resumed. {pausedTasks.Count} task(s) un-paused." });
        }

        /// <summary>Mark a project as Delayed (still running, timeline extended).</summary>
        [HttpPost("project/{id}/delay")]
        public async Task<IActionResult> DelayProject(int id, [FromBody] DecisionRequest req)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.StrategicStatus = ProjectStrategicStatus.Delayed;
            project.StrategicStatusReason = req.Notes;
            project.StrategicStatusChangedAt = DateTime.UtcNow;
            project.StrategicStatusChangedById = CurrentUserId;
            await _context.SaveChangesAsync();
            await LogDecision("DelayProject", id, null, req.Notes);

            return Ok(new { success = true, message = $"Project '{project.Name}' marked as Delayed." });
        }

        /// <summary>Flag a project for acceleration — priority resourcing.</summary>
        [HttpPost("project/{id}/accelerate")]
        public async Task<IActionResult> AccelerateProject(int id, [FromBody] DecisionRequest req)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.StrategicStatus = ProjectStrategicStatus.Accelerate;
            project.StrategicStatusReason = req.Notes;
            project.StrategicStatusChangedAt = DateTime.UtcNow;
            project.StrategicStatusChangedById = CurrentUserId;
            await _context.SaveChangesAsync();
            await LogDecision("AccelerateProject", id, null, req.Notes);

            return Ok(new { success = true, message = $"Project '{project.Name}' flagged for acceleration." });
        }

        /// <summary>Register a new project in Planning status.</summary>
        [HttpPost("project/plan-new")]
        public async Task<IActionResult> PlanNewProject([FromBody] NewProjectRequest req)
        {
            var project = new Project
            {
                Name = req.Name,
                Description = req.Description,
                StrategicStatus = ProjectStrategicStatus.Planning,
                StrategicStatusReason = "Planned via Strategic Hub",
                PlannedStartWeek = req.PlannedStartWeek,
                IsOnExecutiveRadar = true,
                CreatedById = CurrentUserId,
                StrategicStatusChangedAt = DateTime.UtcNow,
                StrategicStatusChangedById = CurrentUserId
            };
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();
            await LogDecision("PlanNewProject", project.Id, null, req.Description);

            return Ok(new { success = true, message = $"Project '{project.Name}' created in Planning status.", projectId = project.Id });
        }

        // ── TASK CONTROLS ───────────────────────────────────────────────────

        /// <summary>Pause a single task with a reason.</summary>
        [HttpPost("task/{id}/pause")]
        public async Task<IActionResult> PauseTask(int id, [FromBody] DecisionRequest req)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            task.IsPaused = true;
            task.PauseReason = req.Notes;
            task.PausedAt = DateTime.UtcNow;
            task.PausedById = CurrentUserId;
            await _context.SaveChangesAsync();
            await LogDecision("PauseTask", task.ProjectId, id, req.Notes);

            return Ok(new { success = true, message = $"Task '{task.Title}' paused." });
        }

        /// <summary>Resume a paused task.</summary>
        [HttpPost("task/{id}/resume")]
        public async Task<IActionResult> ResumeTask(int id, [FromBody] DecisionRequest req)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            task.IsPaused = false;
            task.PauseReason = null;
            task.PausedAt = null;
            task.PausedById = null;
            await _context.SaveChangesAsync();
            await LogDecision("ResumeTask", task.ProjectId, id, req.Notes);

            return Ok(new { success = true, message = $"Task '{task.Title}' resumed." });
        }

        /// <summary>Reassign a task to a different team member.</summary>
        [HttpPost("task/{id}/reassign")]
        public async Task<IActionResult> ReassignTask(int id, [FromBody] ReassignRequest req)
        {
            var task = await _context.Tasks.Include(t => t.Assignee).FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();

            var oldAssignee = task.Assignee?.FullName ?? "Unassigned";
            task.AssigneeId = req.NewAssigneeId;
            await _context.SaveChangesAsync();
            await LogDecision("ReassignTask", task.ProjectId, id, req.Notes,
                $"{{\"from\":\"{oldAssignee}\",\"toId\":\"{req.NewAssigneeId}\"}}");

            return Ok(new { success = true, message = $"Task '{task.Title}' reassigned." });
        }

        /// <summary>Move a task to a different sprint.</summary>
        [HttpPost("task/{id}/move-sprint")]
        public async Task<IActionResult> MoveTaskSprint(int id, [FromBody] MoveSprintRequest req)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            var oldSprintId = task.SprintId;
            task.SprintId = req.NewSprintId;
            await _context.SaveChangesAsync();
            await LogDecision("MoveTaskSprint", task.ProjectId, id, req.Notes,
                $"{{\"fromSprint\":{oldSprintId},\"toSprint\":{req.NewSprintId}}}");

            return Ok(new { success = true, message = $"Task moved to sprint {req.NewSprintId}." });
        }

        /// <summary>Create a new Critical priority task, pre-approved, assigned to a team member.</summary>
        [HttpPost("task/add-priority")]
        public async Task<IActionResult> AddPriorityTask([FromBody] AddPriorityTaskRequest req)
        {
            var task = new Models.TaskItem
            {
                Title = req.Title,
                Description = req.Description,
                Priority = TaskPriority.Critical,
                Status = TaskStatus.Approved, // Bypasses backlog review — manager decision
                AssigneeId = req.AssigneeId,
                ProjectId = req.ProjectId,
                SprintId = req.SprintId,
                DueDate = req.DueDate,
                EstimatedHours = req.EstimatedHours,
                CreatedById = CurrentUserId,
                IsBacklog = false
            };
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();
            await LogDecision("AddPriorityTask", req.ProjectId, task.Id,
                $"Critical task added by manager: {req.Title}");

            return Ok(new { success = true, message = $"Priority task '{task.Title}' created and assigned.", taskId = task.Id });
        }

        /// <summary>Bulk-shift resources: move unstarted tasks of a user from one project to another.</summary>
        [HttpPost("team/shift-resources")]
        public async Task<IActionResult> ShiftResources([FromBody] ShiftResourcesRequest req)
        {
            var tasksToShift = await _context.Tasks
                .Where(t => t.AssigneeId == req.UserId
                    && t.ProjectId == req.FromProjectId
                    && (t.Status == TaskStatus.New || t.Status == TaskStatus.Approved || t.Status == TaskStatus.ToDo))
                .ToListAsync();

            foreach (var task in tasksToShift)
            {
                task.ProjectId = req.ToProjectId;
                task.SprintId = req.ToSprintId; // may be null — puts them in backlog of new project
            }

            await _context.SaveChangesAsync();
            await LogDecision("ShiftResources", req.ToProjectId, null, req.Notes,
                $"{{\"userId\":\"{req.UserId}\",\"fromProject\":{req.FromProjectId},\"toProject\":{req.ToProjectId},\"tasksShifted\":{tasksToShift.Count}}}");

            return Ok(new { success = true, message = $"{tasksToShift.Count} task(s) shifted to new project." });
        }
    }

    // ── Request DTOs ─────────────────────────────────────────────────────────

    public class DecisionRequest
    {
        public string? Notes { get; set; }
    }

    public class ReassignRequest
    {
        public string NewAssigneeId { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class MoveSprintRequest
    {
        public int? NewSprintId { get; set; }
        public string? Notes { get; set; }
    }

    public class AddPriorityTaskRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AssigneeId { get; set; }
        public int? ProjectId { get; set; }
        public int? SprintId { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal EstimatedHours { get; set; }
    }

    public class NewProjectRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? PlannedStartWeek { get; set; }
    }

    public class ShiftResourcesRequest
    {
        public string UserId { get; set; } = string.Empty;
        public int FromProjectId { get; set; }
        public int ToProjectId { get; set; }
        public int? ToSprintId { get; set; }
        public string? Notes { get; set; }
    }
}
