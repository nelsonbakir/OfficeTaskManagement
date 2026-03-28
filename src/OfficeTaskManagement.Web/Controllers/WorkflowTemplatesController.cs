using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.WorkflowEngine;

namespace OfficeTaskManagement.Controllers
{
    /// <summary>
    /// Admin controller for managing Workflow Templates (Fragnets).
    /// Restricted to Manager and Project Coordinator roles only.
    /// </summary>
    [Authorize(Roles = "Manager,Project Coordinator,Project Lead")]
    public class WorkflowTemplatesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWorkflowEngineService _engine;

        public WorkflowTemplatesController(ApplicationDbContext db, IWorkflowEngineService engine)
        {
            _db = db;
            _engine = engine;
        }

        // GET: WorkflowTemplates
        public async Task<IActionResult> Index()
        {
            var templates = await _db.WorkflowTemplates
                .Include(wt => wt.Project)
                .Include(wt => wt.Stages)
                .OrderBy(wt => wt.ProjectId == null ? 0 : 1)
                .ThenBy(wt => wt.Name)
                .ToListAsync();

            return View(templates);
        }

        // GET: WorkflowTemplates/Create
        public IActionResult Create()
        {
            ViewBag.Projects = new SelectList(_db.Projects, "Id", "Name");
            ViewBag.TaskTypes = Enum.GetValues<TaskType>()
                .Select(t => new { Value = (int)t, Text = t.ToString() })
                .ToList();
            return View("Edit", new WorkflowTemplate());
        }

        // POST: WorkflowTemplates/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WorkflowTemplate model)
        {
            if (ModelState.IsValid)
            {
                _db.WorkflowTemplates.Add(model);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Workflow template '{model.Name}' created.";
                return RedirectToAction(nameof(Edit), new { id = model.Id });
            }
            ViewBag.Projects = new SelectList(_db.Projects, "Id", "Name", model.ProjectId);
            ViewBag.TaskTypes = Enum.GetValues<TaskType>()
                .Select(t => new { Value = (int)t, Text = t.ToString() })
                .ToList();
            return View("Edit", model);
        }

        // GET: WorkflowTemplates/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var template = await _db.WorkflowTemplates
                .Include(wt => wt.Stages.OrderBy(s => s.Order))
                .FirstOrDefaultAsync(wt => wt.Id == id);

            if (template == null) return NotFound();

            ViewBag.Projects = new SelectList(_db.Projects, "Id", "Name", template.ProjectId);
            ViewBag.TaskTypes = Enum.GetValues<TaskType>()
                .Select(t => new { Value = (int)t, Text = t.ToString() })
                .ToList();
            ViewBag.RaciRoles = Enum.GetValues<RaciRole>()
                .Select(r => new { Value = (int)r, Text = r.ToString() })
                .ToList();
            ViewBag.DependencyTypes = Enum.GetValues<StageDependency>()
                .Select(d => new { Value = (int)d, Text = d.ToString() })
                .ToList();

            return View(template);
        }

        // POST: WorkflowTemplates/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, WorkflowTemplate model)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                var existing = await _db.WorkflowTemplates.FindAsync(id);
                if (existing == null) return NotFound();

                existing.Name = model.Name;
                existing.Description = model.Description;
                existing.ProjectId = model.ProjectId;
                existing.ApplicableTaskType = model.ApplicableTaskType;
                existing.IsActive = model.IsActive;

                await _db.SaveChangesAsync();
                TempData["Success"] = "Template updated.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            ViewBag.Projects = new SelectList(_db.Projects, "Id", "Name", model.ProjectId);
            return View(model);
        }

        // POST: WorkflowTemplates/AddStage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStage(int templateId, string name, string defaultRoleTitle,
            int order, RaciRole raciRole, StageDependency dependencyType, decimal lagHours, string? definitionOfDone)
        {
            var template = await _db.WorkflowTemplates.FindAsync(templateId);
            if (template == null) return NotFound();

            _db.WorkflowStages.Add(new WorkflowStage
            {
                WorkflowTemplateId = templateId,
                Name               = name,
                DefaultRoleTitle   = defaultRoleTitle,
                Order              = order,
                RaciRole           = raciRole,
                DependencyType     = dependencyType,
                LagHours           = lagHours,
                DefinitionOfDone   = definitionOfDone
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Stage '{name}' added.";
            return RedirectToAction(nameof(Edit), new { id = templateId });
        }

        // POST: WorkflowTemplates/DeleteStage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStage(int stageId, int templateId)
        {
            var stage = await _db.WorkflowStages.FindAsync(stageId);
            if (stage != null)
            {
                _db.WorkflowStages.Remove(stage);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Stage '{stage.Name}' removed.";
            }
            return RedirectToAction(nameof(Edit), new { id = templateId });
        }

        // POST: WorkflowTemplates/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var template = await _db.WorkflowTemplates.FindAsync(id);
            if (template != null)
            {
                _db.WorkflowTemplates.Remove(template);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Template '{template.Name}' deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: WorkflowTemplates/WorkPackage/{taskId}
        /// <summary>
        /// Displays the RACI Work Package view for a parent task — the PM reporting dashboard
        /// showing per-stage PERT estimates, actual hours, variance, and structured audit trail.
        /// </summary>
        [AllowAnonymous]
        [Authorize]
        public async Task<IActionResult> WorkPackage(int id)
        {
            try
            {
                var summary = await _engine.GetWorkPackageSummaryAsync(id);
                var parent = await _db.Tasks
                    .Include(t => t.AccountableUser)
                    .Include(t => t.CreatedBy)
                    .Include(t => t.Project)
                    .Include(t => t.History.OrderByDescending(h => h.Timestamp))
                        .ThenInclude(h => h.ChangedBy)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (parent == null) return NotFound();

                ViewBag.ParentTask = parent;
                return View(summary);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index", "TaskItems");
            }
        }

        // POST: WorkflowTemplates/ApplyTemplate
        /// <summary>Applies a Fragnet template to a parent task, spawning stage sub-tasks.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyTemplate(int taskId, int templateId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            try
            {
                // Set the accountable user to whoever applies the template (PM/Lead)
                var task = await _db.Tasks.FindAsync(taskId);
                if (task != null && task.AccountableUserId == null)
                {
                    task.AccountableUserId = userId;
                    await _db.SaveChangesAsync();
                }

                await _engine.SpawnWorkflowSubTasksAsync(taskId, templateId);
                TempData["Success"] = "Workflow pipeline applied. Stage sub-tasks have been created.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to apply template: {ex.Message}";
            }
            return RedirectToAction(nameof(WorkPackage), new { id = taskId });
        }

        // POST: WorkflowTemplates/TransitionStage
        /// <summary>Triggers a stage gate check and transitions to the next stage.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransitionStage(int subTaskId, int parentTaskId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            try
            {
                await _engine.TransitionStageAsync(subTaskId, userId);
                TempData["Success"] = "Stage gate passed. Next stage is now active.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["GateError"] = ex.Message;
            }
            return RedirectToAction(nameof(WorkPackage), new { id = parentTaskId });
        }
    }
}
