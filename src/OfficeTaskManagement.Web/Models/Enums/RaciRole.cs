namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Defines the RACI responsibility classification for a task or workflow stage participant.
    /// R = Responsible (does the work), A = Accountable (owns the outcome),
    /// C = Consulted (provides input), I = Informed (kept in the loop).
    /// </summary>
    public enum RaciRole
    {
        Responsible = 0,
        Accountable = 1,
        Consulted   = 2,
        Informed    = 3
    }
}
