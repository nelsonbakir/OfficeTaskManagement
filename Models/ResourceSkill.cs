using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// A skill entry belonging to a ResourceProfile.
    /// </summary>
    public class ResourceSkill
    {
        [Key]
        public int Id { get; set; }

        public int ResourceProfileId { get; set; }
        public ResourceProfile? ResourceProfile { get; set; }

        [Required]
        [StringLength(100)]
        public string SkillName { get; set; } = string.Empty;

        public ProficiencyLevel ProficiencyLevel { get; set; } = ProficiencyLevel.Intermediate;
    }
}
