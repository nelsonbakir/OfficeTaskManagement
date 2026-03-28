using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OfficeTaskManagement.Models;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels
{
    public class TaskItemViewModel
    {
        public TaskItem TaskItem { get; set; } = new TaskItem();
        public List<IFormFile>? Attachments { get; set; }
        public SelectList? UsersList { get; set; }
        public SelectList? ProjectsList { get; set; }
        public SelectList? EpicsList { get; set; }
        public SelectList? SprintsList { get; set; }
        public SelectList? FeaturesList { get; set; }
        public SelectList? ParentTasksList { get; set; }
        public SelectList? UserStoriesList { get; set; }
        public MultiSelectList? AreasList { get; set; }
        public List<int> SelectedAreaIds { get; set; } = new List<int>();

        // ── RACI Workflow ────────────────────────────────────────────────────
        /// <summary>Workflow template (Fragnet) options for PM to assign on task creation.</summary>
        public SelectList? WorkflowTemplatesList { get; set; }

        /// <summary>The template ID selected by the PM. If set, the engine spawns stage sub-tasks.</summary>
        public int? SelectedWorkflowTemplateId { get; set; }

        /// <summary>Accountable user options (PM/Lead who owns the work package outcome).</summary>
        public SelectList? AccountableUsersList { get; set; }
        // ─────────────────────────────────────────────────────────────────────
    }
}
