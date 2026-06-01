using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class TasksApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TasksApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetTasks([FromQuery] int? projectId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var query = _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Feature)
                .ThenInclude(f => f.Epic)
                .Include(t => t.Areas)
                .Where(t => t.AssigneeId == userId);

            if (projectId.HasValue)
            {
                query = query.Where(t => t.ProjectId == projectId.Value);
            }

            var tasks = await query
                .Where(t => t.IsBacklog == false && t.Status != OfficeTaskManagement.Models.Enums.TaskStatus.Done)
                .OrderBy(t => t.DueDate).Select(t => new
            {
                t.Id,
                t.Title,
                t.Description,
                Status = (int)t.Status,
                StatusName = t.Status.ToString(),
                Priority = t.Type.ToString(),
                t.DueDate,
                ProjectName = t.Project != null ? t.Project.Name : "Independent",
                FeatureName = t.Feature != null ? t.Feature.Name : null,
                t.EstimatedHours,
                Areas = t.Areas.Select(a => new { a.Id, a.Name }).ToList()
            }).ToListAsync();

            return Ok(tasks);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTaskDetails(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var task = await _context.Tasks
                .Include(t => t.Project)
                .Include(t => t.Feature)
                .Include(t => t.Areas)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound();
            
            if (task.AssigneeId != userId && task.CreatedById != userId && !User.IsInRole("Manager"))
                return Forbid();

            return Ok(new
            {
                task.Id,
                task.Title,
                task.Description,
                Status = (int)task.Status,
                StatusName = task.Status.ToString(),
                Priority = task.Priority.ToString(),
                task.DueDate,
                ProjectName = task.Project != null ? task.Project.Name : "Independent",
                FeatureName = task.Feature != null ? task.Feature.Name : null,
                task.EstimatedHours,
                task.IsBacklog,
                Areas = task.Areas.Select(a => new { a.Id, a.Name }).ToList()
            });
        }

        [HttpGet("{id}/comments")]
        public async Task<IActionResult> GetComments(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();
            if (task.AssigneeId != userId && task.CreatedById != userId && !User.IsInRole("Manager")) return Forbid();

            var comments = await _context.TaskComments
                .Include(c => c.User)
                .Where(c => c.TaskId == id)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new {
                    c.Id,
                    c.CommentText,
                    c.CreatedAt,
                    AuthorName = c.User != null ? c.User.FullName : "Unknown",
                    IsSelf = c.UserId == userId
                }).ToListAsync();

            return Ok(comments);
        }

        public class NewCommentModel
        {
            public string Text { get; set; } = string.Empty;
        }

        [HttpPost("{id}/comments")]
        public async Task<IActionResult> PostComment(int id, [FromBody] NewCommentModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();
            if (task.AssigneeId != userId && task.CreatedById != userId && !User.IsInRole("Manager")) return Forbid();

            if (string.IsNullOrWhiteSpace(model.Text)) return BadRequest();

            var comment = new TaskComment
            {
                TaskId = id,
                UserId = userId!,
                CommentText = model.Text,
                CreatedAt = DateTime.UtcNow
            };

            _context.TaskComments.Add(comment);
            await _context.SaveChangesAsync();
            
            return Ok(new { success = true });
        }

        public class UpdateStatusModel
        {
            public int StatusId { get; set; }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var task = await _context.Tasks
                .Include(t => t.WorkflowStage)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound();
            
            if (task.AssigneeId != userId && task.CreatedById != userId && !User.IsInRole("Manager"))
                return Forbid();

            var newStatus = (OfficeTaskManagement.Models.Enums.TaskStatus)model.StatusId;

            // Block manual status changes on Work Package summary tasks
            if (task.IsWorkPackage)
                return BadRequest(new { error = "Work Package status is managed by stage gates." });

            // ToDo requires an assignee
            if (newStatus == OfficeTaskManagement.Models.Enums.TaskStatus.ToDo && string.IsNullOrEmpty(task.AssigneeId))
                return BadRequest(new { error = "Tasks in ToDo must have an assignee." });

            // Block paused tasks
            if (task.IsPaused)
                return BadRequest(new { error = "This task is paused. Resume it before changing status." });

            var oldStatus = task.Status;
            task.Status = newStatus;
            _context.Update(task);

            // Audit entry
            _context.TaskHistories.Add(new TaskHistory
            {
                TaskItemId = task.Id,
                ChangedById = userId,
                FieldChanged = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                ChangeDescription = $"Status changed from {oldStatus} to {newStatus} via Kanban board.",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Sync parent Work Package if this is a stage sub-task
            if (task.ParentTaskId.HasValue && task.WorkflowStageId.HasValue)
            {
                var engine = HttpContext.RequestServices.GetRequiredService<OfficeTaskManagement.Services.WorkflowEngine.IWorkflowEngineService>();
                await engine.SyncParentStatusAsync(task.ParentTaskId.Value, userId);
            }
            // Regular sub-task: escalate parent to InProgress
            else if (task.ParentTaskId.HasValue && newStatus >= OfficeTaskManagement.Models.Enums.TaskStatus.InProgress)
            {
                var parent = await _context.Tasks.FindAsync(task.ParentTaskId);
                if (parent != null && parent.Status < OfficeTaskManagement.Models.Enums.TaskStatus.InProgress
                    && parent.Status != OfficeTaskManagement.Models.Enums.TaskStatus.Done)
                {
                    parent.Status = OfficeTaskManagement.Models.Enums.TaskStatus.InProgress;
                    _context.Update(parent);
                    await _context.SaveChangesAsync();
                }
            }
            
            return Ok(new { success = true, id = task.Id, status = task.Status.ToString() });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetEligibleUsers()
        {
            var users = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.FullName))
                .Select(u => new
                {
                    id = u.Id,
                    display = u.FullName,
                    email = u.Email
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}
