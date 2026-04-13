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
        Tweaking    = 5
    }
}
