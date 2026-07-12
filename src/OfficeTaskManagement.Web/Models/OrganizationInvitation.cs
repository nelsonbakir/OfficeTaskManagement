using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class OrganizationInvitation : IMustHaveTenant
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Role { get; set; } = "Developer";

        [Required]
        [StringLength(100)]
        public string InviteCode { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

        public bool IsAccepted { get; set; } = false;

        // Navigation
        public Tenant? Tenant { get; set; }
    }
}
