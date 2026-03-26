namespace OfficeTaskManagement.Models.Enums
{
    public enum ProjectStrategicStatus
    {
        Active,     // Running normally
        OnHold,     // Paused by management decision
        Delayed,    // Timeline pushed — still running at reduced pace
        Accelerate, // Management flagged for priority resourcing
        Planning,   // Not yet started — under evaluation
        Cancelled   // Formally terminated
    }
}
