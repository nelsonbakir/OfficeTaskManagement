using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models
{
    public class PublicHoliday
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; }

        /// <summary>True if the holiday occurs on the same day every year (not fully accurate for lunar, but useful for fixed dates like Dec 16).</summary>
        public bool IsFixedDate { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
