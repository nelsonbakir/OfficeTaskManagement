using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Enums;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Services.WorkflowEngine
{
    /// <summary>
    /// Computes the Kanban board column configuration from a project's active
    /// Workflow Template(s). The board dynamically reflects what gate types
    /// exist in the template — not a hardcoded list of statuses.
    ///
    /// Column generation rules:
    ///   1. Anchor columns (ToDo, InProgress, Done) are always included.
    ///   2. For each stage gate type in the project's active template(s),
    ///      the corresponding terminal TaskStatus becomes a board column.
    ///   3. Columns are returned in the canonical order defined by MasterColumns.
    ///   4. If no project is specified (cross-project board), all columns are shown.
    ///   5. If a project has no active template, all columns are shown (safe fallback).
    /// </summary>
    public class KanbanGovernanceService
    {
        private readonly ApplicationDbContext _db;

        /// <summary>
        /// Master ordered column list. All possible Kanban columns in their
        /// correct display order. The service filters this list per project.
        /// </summary>
        public static readonly IReadOnlyList<KanbanColumn> MasterColumns = new List<KanbanColumn>
        {
            new KanbanColumn(TaskStatus.ToDo,       "To Do",       "#A19F9D"),
            new KanbanColumn(TaskStatus.InProgress, "In Progress", "#0078D4"),
            new KanbanColumn(TaskStatus.Committed,  "Committed",   "#C19C00"),
            new KanbanColumn(TaskStatus.Reviewed,   "Reviewed",    "#8764B8"),
            new KanbanColumn(TaskStatus.Tested,     "Tested",      "#498205"),
            new KanbanColumn(TaskStatus.Done,       "Done",        "#107C10"),
        };

        public KanbanGovernanceService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns the ordered list of Kanban columns applicable for the given project.
        /// Reads the active Workflow Template(s) for the project and maps gate types
        /// to their terminal TaskStatus values to build the column set.
        /// </summary>
        /// <param name="projectId">
        /// The project to compute columns for. Pass null for a cross-project view
        /// (returns all columns so every status can be represented).
        /// </param>
        public async Task<IReadOnlyList<KanbanColumn>> GetColumnsAsync(int? projectId)
        {
            // Cross-project or no context: show all possible columns
            if (!projectId.HasValue)
                return MasterColumns;

            // Discover gate types used by all active templates for this project
            var gateTypes = await _db.WorkflowTemplates
                .Where(t => t.ProjectId == projectId && t.IsActive)
                .SelectMany(t => t.Stages)
                .Select(s => s.GateType)
                .Distinct()
                .ToListAsync();

            // No template → show full default column set (safe fallback)
            if (!gateTypes.Any())
                return MasterColumns;

            // Build required column set: always include anchors
            var required = new HashSet<TaskStatus>
            {
                TaskStatus.ToDo,
                TaskStatus.InProgress,
                TaskStatus.Done
            };

            foreach (var gate in gateTypes)
            {
                var terminal = StageLifecycleMap.TerminalStatus(gate);
                if (terminal.HasValue)
                    required.Add(terminal.Value);
            }

            // Return in canonical order
            return MasterColumns.Where(c => required.Contains(c.Status)).ToList();
        }

        /// <summary>
        /// Synchronous version for use in non-async contexts (e.g. Razor views
        /// when columns were pre-computed and passed via ViewBag).
        /// Returns the default full column set.
        /// </summary>
        public static IReadOnlyList<KanbanColumn> DefaultColumns() => MasterColumns;
    }

    /// <summary>
    /// Represents a single column on the Kanban board, carrying the TaskStatus
    /// it maps to, its display label, and an accent color for styling.
    /// </summary>
    public record KanbanColumn(TaskStatus Status, string Label, string AccentColor);
}
