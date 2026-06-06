namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// A single granular permission key belonging to a PermissionGroup.
    /// Keys are strings like "projects.manage" or "salary.view".
    /// </summary>
    public class PermissionGroupKey : IMustHaveTenant
    {
        public string TenantId { get; set; } = string.Empty;
        public int Id { get; set; }

        public int PermissionGroupId { get; set; }

        /// <summary>
        /// The permission key string. Must match a value in <see cref="Permissions"/>.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        // ── Navigation ────────────────────────────────────────────────────────
        public PermissionGroup PermissionGroup { get; set; } = null!;
    }

    /// <summary>
    /// Central catalogue of all well-known permission keys used throughout the application.
    /// Controllers and views reference these constants to avoid magic strings.
    /// </summary>
    public static class Permissions
    {
        // ── Users & Roles ─────────────────────────────────────────────────────
        public const string UsersView   = "users.view";
        public const string UsersManage = "users.manage";
        public const string RolesManage = "roles.manage";

        // ── Projects ──────────────────────────────────────────────────────────
        public const string ProjectsView   = "projects.view";
        public const string ProjectsManage = "projects.manage";

        // ── Analytics ─────────────────────────────────────────────────────────
        public const string AnalyticsView = "analytics.view";
        public const string AnalyticsAI   = "analytics.ai";

        // ── Strategic ─────────────────────────────────────────────────────────
        public const string StrategicView   = "strategic.view";
        public const string StrategicManage = "strategic.manage";

        // ── Resources ─────────────────────────────────────────────────────────
        public const string ResourcesView   = "resources.view";
        public const string ResourcesManage = "resources.manage";

        // ── Salary ────────────────────────────────────────────────────────────
        public const string SalaryView   = "salary.view";
        public const string SalaryManage = "salary.manage";

        // ── Capacity ──────────────────────────────────────────────────────────
        public const string CapacityView = "capacity.view";

        // ── System / Holidays ─────────────────────────────────────────────────
        public const string HolidaysManage = "holidays.manage";

        // ── Planning ──────────────────────────────────────────────────────────
        public const string EpicsManage    = "epics.manage";
        public const string FeaturesManage = "features.manage";
        public const string SprintsManage  = "sprints.manage";

        // ── Work ──────────────────────────────────────────────────────────────
        public const string TasksManage = "tasks.manage";

        // ── Quality ───────────────────────────────────────────────────────────
        public const string TestCasesManage = "testcases.manage";

        // ── Workflow ──────────────────────────────────────────────────────────
        public const string WorkflowManage = "workflow.manage";

        /// <summary>Ordered list of all known permission keys for the UI permission picker.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            UsersView, UsersManage, RolesManage,
            ProjectsView, ProjectsManage,
            AnalyticsView, AnalyticsAI,
            StrategicView, StrategicManage,
            ResourcesView, ResourcesManage,
            SalaryView, SalaryManage,
            CapacityView,
            HolidaysManage,
            EpicsManage, FeaturesManage, SprintsManage,
            TasksManage,
            TestCasesManage,
            WorkflowManage
        };
    }
}
