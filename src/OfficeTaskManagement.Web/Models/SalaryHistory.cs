using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OfficeTaskManagement.Models.Enums;

namespace OfficeTaskManagement.Models
{
    /// <summary>
    /// Append-only ledger of salary / rate changes for a resource.
    /// Provides temporal integrity: cost reports always resolve the rate that
    /// was <em>effective on the date of work</em>, not the current rate.
    ///
    /// Invariant: exactly one record per ResourceProfile has EffectiveTo = null
    /// (the currently-active record).  When a new record is added, the previous
    /// active record's EffectiveTo is set to (newRecord.EffectiveFrom - 1 day).
    /// </summary>
    public class SalaryHistory
    {
        [Key]
        public int Id { get; set; }

        // ── Resource Link ───────────────────────────────────────────────────
        [Required]
        public int ResourceProfileId { get; set; }
        public ResourceProfile ResourceProfile { get; set; } = null!;

        // ── Compensation Definition ─────────────────────────────────────────

        /// <summary>How the raw <see cref="Amount"/> is structured.</summary>
        public SalaryType SalaryType { get; set; }

        /// <summary>
        /// Raw compensation value in <see cref="Currency"/> units.
        /// Meaning depends on <see cref="SalaryType"/>:
        ///   MonthlySalary / Stipend → gross per month
        ///   AnnualSalary            → gross per year
        ///   DailyRate               → per working day
        ///   HourlyRate              → per hour
        /// </summary>
        [Required]
        [Range(0, 99_999_999)]
        [Column(TypeName = "decimal(14,2)")]
        public decimal Amount { get; set; }

        /// <summary>ISO 4217 currency code (e.g., "BDT", "USD").</summary>
        [StringLength(3)]
        public string Currency { get; set; } = "BDT";

        /// <summary>
        /// Derived hourly cost rate, stored at write-time so future changes to
        /// DailyCapacityHours or workingDays config do not retroactively alter
        /// historical analytics.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(10,4)")]
        public decimal EffectiveHourlyRate { get; set; }

        /// <summary>
        /// Optional client-facing bill rate (what the client is charged per hour).
        /// Only relevant for Contractual / Freelance / Consultant resource types.
        /// Null for salaried employees.
        /// </summary>
        [Column(TypeName = "decimal(10,4)")]
        public decimal? BillRate { get; set; }

        // ── Temporal Bounds ─────────────────────────────────────────────────

        /// <summary>Date from which this rate applies (inclusive, local midnight UTC).</summary>
        [Required]
        public DateTime EffectiveFrom { get; set; }

        /// <summary>
        /// Date on which this rate is superseded (exclusive).
        /// Null means this is the currently-active record.
        /// Set to (nextRecord.EffectiveFrom - 1 day) when a new record is added.
        /// </summary>
        public DateTime? EffectiveTo { get; set; }

        // ── Audit ───────────────────────────────────────────────────────────

        /// <summary>Free-text reason (e.g., "Annual increment", "Promotion to Senior Dev").</summary>
        [StringLength(500)]
        public string? Reason { get; set; }

        /// <summary>Manager / Admin who recorded this change.</summary>
        public string? RecordedById { get; set; }
        public User? RecordedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Computed helpers (not mapped) ───────────────────────────────────

        /// <summary>True when this is the currently-active salary record.</summary>
        [NotMapped]
        public bool IsCurrent => EffectiveTo == null;
    }
}
