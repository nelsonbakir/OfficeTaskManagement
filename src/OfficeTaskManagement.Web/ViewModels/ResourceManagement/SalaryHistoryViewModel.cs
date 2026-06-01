using System;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.ViewModels.ResourceManagement
{
    /// <summary>
    /// Read-only representation of one SalaryHistory ledger entry,
    /// used in the timeline view and profile sidebar.
    /// </summary>
    public class SalaryHistoryViewModel
    {
        public int Id { get; set; }
        public int ResourceProfileId { get; set; }
        public string ResourceFullName { get; set; } = string.Empty;

        public SalaryType SalaryType { get; set; }

        /// <summary>Raw compensation amount (monthly / annual / daily / hourly).</summary>
        public decimal Amount { get; set; }

        /// <summary>Pre-computed hourly rate stored at write-time.</summary>
        public decimal EffectiveHourlyRate { get; set; }

        /// <summary>Optional client bill rate (Contractual/Freelance/Consultant only).</summary>
        public decimal? BillRate { get; set; }

        public string Currency { get; set; } = "BDT";

        public DateTime EffectiveFrom { get; set; }

        /// <summary>Null = currently active record.</summary>
        public DateTime? EffectiveTo { get; set; }

        /// <summary>True when this is the currently-active salary record.</summary>
        public bool IsCurrent => EffectiveTo == null;

        /// <summary>Reason for the change (e.g., "Annual increment", "Promotion").</summary>
        public string? Reason { get; set; }

        public string? RecordedByName { get; set; }
        public DateTime CreatedAt { get; set; }

        // ── Display helpers ─────────────────────────────────────────────────

        public string SalaryTypeLabel => SalaryType switch
        {
            SalaryType.MonthlySalary => "Monthly Salary",
            SalaryType.AnnualSalary  => "Annual Salary",
            SalaryType.DailyRate     => "Daily Rate",
            SalaryType.HourlyRate    => "Hourly Rate",
            SalaryType.Stipend       => "Stipend",
            _                        => SalaryType.ToString()
        };

        public string PeriodLabel =>
            EffectiveTo.HasValue
                ? $"{EffectiveFrom:dd MMM yyyy} – {EffectiveTo.Value:dd MMM yyyy}"
                : $"{EffectiveFrom:dd MMM yyyy} – Present";
    }
}
