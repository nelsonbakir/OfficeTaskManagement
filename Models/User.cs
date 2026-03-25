using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace OfficeTaskManagement.Models
{
    public class User : IdentityUser
    {
        public string? FullName { get; set; }
        public string? AvatarPath { get; set; }

        // ── Resource Management ──────────────────────────────────────────────
        public ResourceProfile? ResourceProfile { get; set; }
        public ICollection<ProjectResourceAllocation> ProjectAllocations { get; set; } = new List<ProjectResourceAllocation>();
        public ICollection<ResourceAvailabilityBlock> AvailabilityBlocks { get; set; } = new List<ResourceAvailabilityBlock>();
        // ────────────────────────────────────────────────────────────────────
    }
}
