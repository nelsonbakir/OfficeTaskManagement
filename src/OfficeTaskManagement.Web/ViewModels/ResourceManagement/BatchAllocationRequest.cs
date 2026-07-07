using System;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    /// <summary>
    /// Request body for <c>Resource/BatchUpsertAllocation</c>.
    /// Allows creating or updating multiple <see cref="OfficeTaskManagement.Models.ProjectResourceAllocation"/>
    /// records in a single HTTP POST from the enhanced Allocate page.
    /// </summary>
    public class BatchAllocationRequest
    {
        public int ProjectId { get; set; }
        public List<BatchAllocationEntry> Entries { get; set; } = new();
    }

    public class BatchAllocationEntry
    {
        public string UserId { get; set; } = string.Empty;
        public int AllocationPercentage { get; set; } = 100;
        public string? ProjectRole { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime? EndDate { get; set; }
    }
}
