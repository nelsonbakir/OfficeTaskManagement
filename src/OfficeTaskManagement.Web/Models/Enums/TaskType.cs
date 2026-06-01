namespace OfficeTaskManagement.Models.Enums
{
    public enum TaskType
    {
        /// <summary>
        /// Sentinel — default(TaskType). Never used as a real task type.
        /// Surfaces deserialization gaps (e.g., missing field in API payload).
        /// </summary>
        Unknown     = 0,
        NewRequest  = 1,
        Enhancement = 2,
        Bug         = 3,
        Hotfix      = 4,
        Tweaking    = 5,
        /// <summary>
        /// A decomposed work unit managed by a RACI workflow template.
        /// The parent task is an Accountable-only Summary Task.
        /// </summary>
        WorkPackage = 6,
        /// <summary>
        /// A stage sub-task spawned by the WorkflowEngine within a Work Package.
        /// Represents one atomic activity owned by a single Responsible party.
        /// </summary>
        Activity    = 7
    }
}
