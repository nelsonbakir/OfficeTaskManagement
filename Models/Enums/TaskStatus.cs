namespace OfficeTaskManagement.Models.Enums
{
    public enum TaskStatus
    {
        New,        // 0 (Backlog, unassigned)
        ToDo,       // 1 (Assigned to anyone)
        InProgress, // 2 (Started by assignee)
        Committed,  // 3 (Delivered by assignee for testing)
        Tested,     // 4 (Tested by QA)
        Done        // 5 (Product Owner confirmed based on test cases)
    }
}
