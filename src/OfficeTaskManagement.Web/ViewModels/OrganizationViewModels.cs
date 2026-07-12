using System;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels.Organization
{
    public class OrganizationIndexViewModel
    {
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        
        // Statistics
        public int UserCount { get; set; }
        public int ProjectCount { get; set; }
        public int SprintCount { get; set; }

        public List<MemberViewModel> Members { get; set; } = new List<MemberViewModel>();
        public List<InvitationViewModel> PendingInvitations { get; set; } = new List<InvitationViewModel>();
    }

    public class MemberViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
        public string AvatarPath { get; set; } = string.Empty;
    }

    public class InvitationViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string InviteCode { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
