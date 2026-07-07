using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    /// <summary>
    /// Thin REST façade for the AI Sprint Planner Wizard embedded in Projects/Details (Sprints tab).
    /// Each endpoint corresponds to one wizard step. All business logic lives in
    /// <see cref="IGeminiAiService"/> and <see cref="ICapacityPlanningService"/>.
    /// </summary>
    [ApiController]
    [Route("api/sprint-planner")]
    [Authorize]
    public class SprintPlannerApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeminiAiService _ai;
        private readonly ICapacityPlanningService _capacity;
        private readonly IResourceService _resourceService;
        private readonly ILogger<SprintPlannerApiController> _logger;

        public SprintPlannerApiController(
            ApplicationDbContext db,
            IGeminiAiService ai,
            ICapacityPlanningService capacity,
            IResourceService resourceService,
            ILogger<SprintPlannerApiController> logger)
        {
            _db             = db;
            _ai             = ai;
            _capacity       = capacity;
            _resourceService = resourceService;
            _logger         = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // ── GET /api/sprint-planner/backlog/{projectId} ───────────────────────
        /// <summary>
        /// Returns unassigned backlog tasks for a project — used to populate the
        /// wizard's backlog preview before Step 1 analysis.
        /// </summary>
        [HttpGet("backlog/{projectId:int}")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> GetBacklogAsync(int projectId, CancellationToken ct)
        {
            var tasks = await _db.Tasks
                .Where(t => t.ProjectId == projectId
                         && t.IsBacklog
                         && t.SprintId == null
                         && t.Status != Models.Enums.TaskStatus.Done)
                .OrderByDescending(t => t.Priority)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Description,
                    Priority = t.Priority.ToString(),
                    t.EstimatedHours,
                    t.PertEstimatedHours,
                    StoryPoints = 3,
                    Status = t.Status.ToString()
                })
                .ToListAsync(ct);

            return Ok(new { tasks, count = tasks.Count });
        }

        // ── POST /api/sprint-planner/propose-goal ─────────────────────────────
        /// <summary>
        /// Step 1 AI call: analyses backlog + sprint window, returns a sprint goal proposal.
        /// </summary>
        [HttpPost("propose-goal")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> ProposeGoalAsync(
            [FromBody] ProposeSprintGoalRequest req, CancellationToken ct)
        {
            if (req.StartDate >= req.EndDate)
                return BadRequest("StartDate must be before EndDate.");

            var goal = await _ai.ProposeSprintGoalAsync(req.ProjectId, req.StartDate, req.EndDate, ct);
            return Ok(goal);
        }

        // ── GET /api/sprint-planner/capacity/{projectId} ─────────────────────
        /// <summary>
        /// Step 2: Returns team capacity slots for a given date window.
        /// Query params: startDate, endDate (ISO 8601).
        /// </summary>
        [HttpGet("capacity/{projectId:int}")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> GetCapacityAsync(
            int projectId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken ct)
        {
            if (startDate >= endDate)
                return BadRequest("startDate must be before endDate.");

            // Ensure UTC
            startDate = startDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc)
                : startDate.ToUniversalTime();
            endDate = endDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
                : endDate.ToUniversalTime();

            // Fetch all allocations for this project in this window
            var allocations = await _db.ProjectResourceAllocations
                .Include(a => a.User)
                    .ThenInclude(u => u!.ResourceProfile)
                        .ThenInclude(rp => rp!.Skills)
                .Where(a => a.ProjectId == projectId
                         && a.StartDate <= endDate
                         && (a.EndDate == null || a.EndDate >= startDate))
                .ToListAsync(ct);

            var slots = new List<ResourceCapacitySlotDto>();
            decimal totalAvailable = 0;
            decimal totalAllocated = 0;

            foreach (var alloc in allocations)
            {
                if (alloc.User == null) continue;

                // Intersect allocation window with sprint window
                var windowStart = alloc.StartDate > startDate ? alloc.StartDate : startDate;
                var windowEnd   = (alloc.EndDate.HasValue && alloc.EndDate.Value < endDate) ? alloc.EndDate.Value : endDate;

                var availableHours = await _resourceService.GetUserAvailableHoursAsync(alloc.UserId, windowStart, windowEnd);
                var allocatedHours = availableHours * (alloc.AllocationPercentage / 100m);

                // Current cross-project load percentage
                var now = DateTime.UtcNow;
                var currentLoadPct = (decimal)await _db.ProjectResourceAllocations
                    .Where(a => a.UserId == alloc.UserId
                             && a.StartDate <= now
                             && (a.EndDate == null || a.EndDate >= now))
                    .SumAsync(a => a.AllocationPercentage, ct);

                var skills = alloc.User.ResourceProfile?.Skills
                    .Select(s => s.SkillName)
                    .ToArray() ?? Array.Empty<string>();

                slots.Add(new ResourceCapacitySlotDto(
                    UserId:          alloc.UserId,
                    FullName:        alloc.User.FullName ?? alloc.User.UserName ?? "Unknown",
                    AvatarPath:      alloc.User.AvatarPath,
                    Role:            alloc.ProjectRole,
                    AvailableHours:  Math.Round(availableHours, 1),
                    AllocatedHours:  Math.Round(allocatedHours, 1),
                    CurrentLoadPct:  Math.Round(currentLoadPct, 0),
                    Skills:          skills));

                totalAvailable += availableHours;
                totalAllocated += allocatedHours;
            }

            var response = new SprintCapacityGateDto
            {
                TotalAvailableHours  = Math.Round(totalAvailable, 1),
                TotalAllocatedHours  = Math.Round(totalAllocated, 1),
                IsTeamOverAllocated  = slots.Any(s => s.CurrentLoadPct > 100),
                Resources            = slots
            };

            return Ok(response);
        }

        // ── GET /api/sprint-planner/capacity-diagnostics/{projectId} ──────────
        /// <summary>
        /// Called by the wizard when the capacity endpoint returns an empty resource list.
        /// Returns a machine-readable <c>cause</c> code so the front-end can display a
        /// targeted prerequisite panel with the correct deep-link action instead of a
        /// generic error message.
        /// <list type="bullet">
        ///   <item><c>NO_ALLOCATIONS</c> — no <see cref="ProjectResourceAllocation"/> rows
        ///     exist for this project at all.</item>
        ///   <item><c>DATE_MISMATCH</c> — allocations exist but none overlap the sprint
        ///     window (startDate/endDate).</item>
        ///   <item><c>UNKNOWN</c> — allocations and dates are fine; likely a data/profile
        ///     issue that needs manual investigation.</item>
        /// </list>
        /// </summary>
        [HttpGet("capacity-diagnostics/{projectId:int}")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> GetCapacityDiagnosticsAsync(
            int projectId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken ct)
        {
            // Ensure UTC so date comparisons are consistent with allocation records
            startDate = startDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc) : startDate.ToUniversalTime();
            endDate = endDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc) : endDate.ToUniversalTime();

            // 1. Any allocations at all for this project?
            var totalAllocations = await _db.ProjectResourceAllocations
                .CountAsync(a => a.ProjectId == projectId, ct);

            if (totalAllocations == 0)
                return Ok(new
                {
                    cause = "NO_ALLOCATIONS",
                    message = "No team members have been formally allocated to this project yet."
                });

            // 2. Any allocations overlapping the selected sprint window?
            var overlapping = await _db.ProjectResourceAllocations
                .CountAsync(a => a.ProjectId == projectId
                              && a.StartDate <= endDate
                              && (a.EndDate == null || a.EndDate >= startDate), ct);

            if (overlapping == 0)
                return Ok(new
                {
                    cause = "DATE_MISMATCH",
                    message = "Team members are allocated to this project but none of their allocation periods cover the selected sprint window."
                });

            // 3. Allocations and dates are fine — data or profile issue
            return Ok(new
            {
                cause = "UNKNOWN",
                message = "Allocations exist for the selected window but capacity data could not be computed. Check resource profiles."
            });
        }

        // ── POST /api/sprint-planner/select-backlog ───────────────────────────
        /// <summary>
        /// Step 3 AI call: selects and PERT-sizes backlog tasks to fit within capacity.
        /// </summary>
        [HttpPost("select-backlog")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> SelectBacklogAsync(
            [FromBody] SelectSprintBacklogRequest req, CancellationToken ct)
        {
            if (req.TotalCapacityHours <= 0)
                return BadRequest("TotalCapacityHours must be positive.");

            var tasks = await _ai.SelectSprintBacklogAsync(
                req.ProjectId, req.StartDate, req.EndDate, req.TotalCapacityHours, ct);

            var totalHours = tasks.Where(t => t.Selected).Sum(t => t.PertHours);
            var utilizationPct = req.TotalCapacityHours > 0
                ? Math.Round((totalHours / req.TotalCapacityHours) * 100, 1)
                : 0;

            var response = new SprintBacklogSelectionDto
            {
                SuggestedTasks      = tasks,
                TotalSelectedHours  = Math.Round(totalHours, 1),
                CapacityHours       = req.TotalCapacityHours,
                UtilizationPct      = utilizationPct,
                SelectionRationale  = $"AI selected {tasks.Count} tasks using {totalHours:F1} of {req.TotalCapacityHours:F1} available hours ({utilizationPct:F0}% utilization)."
            };

            return Ok(response);
        }

        // ── POST /api/sprint-planner/assign-tasks ─────────────────────────────
        /// <summary>
        /// Step 4 AI call: matches tasks to team members by skill and load.
        /// </summary>
        [HttpPost("assign-tasks")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> AssignTasksAsync(
            [FromBody] AssignSprintTasksRequest req, CancellationToken ct)
        {
            var assignments = await _ai.AssignSprintTasksAsync(req.ProjectId, req.Tasks, req.Resources, ct);
            return Ok(assignments);
        }

        // ── POST /api/sprint-planner/confirm ──────────────────────────────────
        /// <summary>
        /// Step 5 (final): persists the full AI-planned sprint in a single DB transaction.
        /// Creates the sprint record, creates new tasks, and assigns existing tasks.
        /// </summary>
        [HttpPost("confirm")]
        [HasPermission(Permissions.SprintsManage)]
        public async Task<IActionResult> ConfirmAsync(
            [FromBody] ConfirmSprintPlanRequest req, CancellationToken ct)
        {
            if (req.Sprint == null || string.IsNullOrWhiteSpace(req.Sprint.Name))
                return BadRequest("Sprint name is required.");
            if (req.Sprint.StartDate >= req.Sprint.EndDate)
                return BadRequest("StartDate must be before EndDate.");

            // Resolve tenant
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == req.ProjectId, ct);
            if (project == null) return NotFound("Project not found.");

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1. Create the sprint
                var sprint = new Sprint
                {
                    ProjectId            = req.ProjectId,
                    TenantId             = project.TenantId,
                    Name                 = req.Sprint.Name,
                    StartDate            = EnsureUtc(req.Sprint.StartDate),
                    EndDate              = EnsureUtc(req.Sprint.EndDate),
                    IsActive             = true,
                    PlannedCapacityHours = req.Sprint.PlannedCapacityHours,
                    AiGeneratedGoal      = req.Sprint.Goal,
                    AiPlanSessionId      = Guid.NewGuid(),
                    TeamNotes            = $"AI-planned sprint created by {UserId} on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
                };
                _db.Sprints.Add(sprint);
                await _db.SaveChangesAsync(ct); // need sprint.Id before adding tasks

                int tasksCreated  = 0;
                int tasksAssigned = 0;

                foreach (var taskDto in req.Sprint.Tasks)
                {
                    var priority = Enum.TryParse<TaskPriority>(taskDto.Priority, true, out var p) ? p : TaskPriority.Medium;
                    var pert = taskDto.EstimatedHours > 0
                        ? taskDto.EstimatedHours
                        : Math.Round((taskDto.OptimisticHours + 4 * taskDto.MostLikelyHours + taskDto.PessimisticHours) / 6, 2);

                    if (taskDto.IsNewTask || taskDto.TaskId == null)
                    {
                        // 2a. Create brand-new task
                        var newTask = new TaskItem
                        {
                            TenantId                   = project.TenantId,
                            Title                      = taskDto.Title,
                            Description                = taskDto.Description,
                            Priority                   = priority,
                            Status                     = Models.Enums.TaskStatus.New,
                            SprintId                   = sprint.Id,
                            ProjectId                  = req.ProjectId,
                            AssigneeId                 = string.IsNullOrEmpty(taskDto.AssigneeId) ? null : taskDto.AssigneeId,
                            EstimatedHours             = pert,
                            EstimatedOptimisticHours   = taskDto.OptimisticHours > 0 ? taskDto.OptimisticHours : null,
                            EstimatedMostLikelyHours   = taskDto.MostLikelyHours > 0 ? taskDto.MostLikelyHours : null,
                            EstimatedPessimisticHours  = taskDto.PessimisticHours > 0 ? taskDto.PessimisticHours : null,
                            PertEstimatedHours         = pert,
                            CreatedById                = UserId,
                            CreatedAt                  = DateTime.UtcNow,
                            IsBacklog                  = false
                        };
                        _db.Tasks.Add(newTask);
                        tasksCreated++;
                    }
                    else
                    {
                        // 2b. Assign existing backlog task to sprint
                        var existingTask = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskDto.TaskId, ct);
                        if (existingTask != null)
                        {
                            existingTask.SprintId    = sprint.Id;
                            existingTask.IsBacklog   = false;
                            existingTask.Priority    = priority;
                            if (!string.IsNullOrEmpty(taskDto.AssigneeId))
                                existingTask.AssigneeId = taskDto.AssigneeId;
                            if (pert > 0) existingTask.EstimatedHours = pert;
                            if (taskDto.OptimisticHours > 0)  existingTask.EstimatedOptimisticHours  = taskDto.OptimisticHours;
                            if (taskDto.MostLikelyHours > 0)  existingTask.EstimatedMostLikelyHours  = taskDto.MostLikelyHours;
                            if (taskDto.PessimisticHours > 0) existingTask.EstimatedPessimisticHours = taskDto.PessimisticHours;
                            if (pert > 0)                      existingTask.PertEstimatedHours        = pert;
                            tasksAssigned++;
                        }
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "AI Sprint Planner: sprint {SprintId} created for project {ProjectId} by {UserId}. Tasks created: {Created}, assigned: {Assigned}.",
                    sprint.Id, req.ProjectId, UserId, tasksCreated, tasksAssigned);

                return Ok(new ConfirmSprintPlanResponse
                {
                    SprintId      = sprint.Id,
                    SprintName    = sprint.Name,
                    TasksCreated  = tasksCreated,
                    TasksAssigned = tasksAssigned
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "AI Sprint Planner confirm failed for project {ProjectId}", req.ProjectId);
                return StatusCode(500, $"Failed to save sprint plan: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static DateTime EnsureUtc(DateTime dt) =>
            dt.Kind switch
            {
                DateTimeKind.Utc   => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _                  => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
    }
}
