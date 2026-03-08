namespace OfficeTaskManagement.Models.Enums
{
    public enum TaskStatus
    {
        New,        // 0 (Backlog, unassigned)
        Approved,   // 1 (Backlog, can be assigned)
        ToDo,       // 2 (Assigned to anyone)
        InProgress, // 3 (Started by assignee)
        Committed,  // 4 (Delivered by assignee for testing)
        Tested,     // 5 (Tested by QA)
        Done        // 6 (Product Owner confirmed based on test cases)
    }
}
