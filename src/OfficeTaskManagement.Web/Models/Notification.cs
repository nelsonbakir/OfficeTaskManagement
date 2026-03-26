using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OfficeTaskManagement.Models
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [Required]
        [StringLength(500)]
        public string Message { get; set; }

        [StringLength(255)]
        public string Link { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string Type { get; set; } // "Assignment", "Mention", "StatusUpdate"
    }
}
