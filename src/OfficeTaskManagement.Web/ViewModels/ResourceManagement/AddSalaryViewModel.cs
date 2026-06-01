using System;
using System.ComponentModel.DataAnnotations;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    /// <summary>
    /// Form ViewModel for recording a new salary / rate record.
    /// Maps to ResourceController.AddSalary (GET + POST).
    /// </summary>
    public class AddSalaryViewModel
    {
        [Required]
        public int ResourceProfileId { get; set; }

        /// <summary>Displayed on the form for context (read-only).</summary>
        public string ResourceFullName { get; set; } = string.Empty;

        /// <summary>Current resource type — affects which fields are shown.</summary>
        public ResourceType ResourceType { get; set; }

        // ── New Rate Definition ─────────────────────────────────────────────

        [Required]
        [Display(Name = "Salary / Rate Type")]
        public SalaryType SalaryType { get; set; } = SalaryType.MonthlySalary;

        [Required]
        [Range(0, 99_999_999, ErrorMessage = "Amount must be between 0 and 99,999,999.")]
        [Display(Name = "Amount")]
        public decimal Amount { get; set; }

        [Display(Name = "Currency")]
        [StringLength(3)]
        public string Currency { get; set; } = "BDT";

        /// <summary>
        /// Applicable for Contractual / Freelance / Consultant resource types.
        /// Represents what the client is billed per hour.
        /// </summary>
        [Range(0, 99_999_999)]
        [Display(Name = "Bill Rate (per hour, optional)")]
        public decimal? BillRate { get; set; }

        // ── Effective Date ──────────────────────────────────────────────────

        [Required]
        [Display(Name = "Effective From")]
        [DataType(DataType.Date)]
        public DateTime EffectiveFrom { get; set; } = DateTime.Today;

        // ── Audit ───────────────────────────────────────────────────────────

        [StringLength(500)]
        [Display(Name = "Reason for Change")]
        public string? Reason { get; set; }

        // ── Read-only context shown on the form ─────────────────────────────

        /// <summary>Daily capacity hours — used for server-side preview calculation.</summary>
        public decimal DailyCapacityHours { get; set; } = 8;

        /// <summary>
        /// Server-computed preview of the derived hourly rate.
        /// Populated after form submission validation, or via AJAX on client.
        /// </summary>
        public decimal PreviewHourlyRate { get; set; }

        /// <summary>Current active rate for reference.</summary>
        public decimal CurrentHourlyRate { get; set; }

        /// <summary>Current salary amount for reference display on the form.</summary>
        public decimal CurrentSalaryAmount { get; set; }

        /// <summary>Current salary type for reference display on the form.</summary>
        public SalaryType CurrentSalaryType { get; set; }

        /// <summary>Whether the effective date is in the past (retroactive warning).</summary>
        public bool IsRetroactive => EffectiveFrom.Date < DateTime.Today;
    }
}
