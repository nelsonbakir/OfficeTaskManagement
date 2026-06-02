using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.ViewModels.RoleManagement
{
    public class CreatePermissionGroupViewModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>Selected permission keys for this group.</summary>
        public List<string> SelectedKeys { get; set; } = new();

        // For the UI picker
        public IReadOnlyList<string> AllKnownKeys { get; set; } = new List<string>();
    }

    public class EditPermissionGroupViewModel
    {
        public int Id { get; set; }
        public bool IsSystemGroup { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public List<string> SelectedKeys { get; set; } = new();
        public IReadOnlyList<string> AllKnownKeys { get; set; } = new List<string>();
    }
}
