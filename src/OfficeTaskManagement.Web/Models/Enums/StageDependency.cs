namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Defines the dependency relationship between two consecutive workflow stages,
    /// following PMP scheduling logic (PDM — Precedence Diagramming Method).
    /// </summary>
    public enum StageDependency
    {
        /// <summary>
        /// Successor stage can only START after predecessor stage FINISHES. (Default — Hard Logic)
        /// </summary>
        FinishToStart = 0,

        /// <summary>
        /// Successor stage can START at the same time as the predecessor (parallel / fast-tracking).
        /// </summary>
        StartToStart  = 1
    }
}
