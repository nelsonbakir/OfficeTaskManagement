using System;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Records a period when a user is unavailable (leave, holidays, training, etc.).
    /// Used by capacity calculation to subtract from available hours.
    /// </summary>
    public class ResourceAvailabilityBlock
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        public AvailabilityBlockReason Reason { get; set; } = AvailabilityBlockReason.Leave;

        [StringLength(500)]
        public string? Notes { get; set; }

        public string? CreatedById { get; set; }
        public User? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Link to resource profile
        public int? ResourceProfileId { get; set; }
        public ResourceProfile? ResourceProfile { get; set; }
    }
}
