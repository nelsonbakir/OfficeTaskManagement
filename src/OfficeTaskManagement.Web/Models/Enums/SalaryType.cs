namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Describes how a resource's compensation is structured.
    /// Used to derive an effective hourly rate for cost analytics.
    /// </summary>
    public enum SalaryType
    {
        /// <summary>
        /// Fixed gross amount per calendar month.
        /// Hourly = Amount / (workingDaysPerMonth × dailyHours).
        /// Typical for: FullTime, PartTime.
        /// </summary>
        MonthlySalary = 0,

        /// <summary>
        /// Fixed gross amount per year.
        /// Hourly = Amount / (workingDaysPerMonth × 12 × dailyHours).
        /// Typical for: FullTime senior/executive contracts.
        /// </summary>
        AnnualSalary = 1,

        /// <summary>
        /// Fixed amount per working day.
        /// Hourly = Amount / dailyHours.
        /// Typical for: Contractual, Consultant, Freelance.
        /// </summary>
        DailyRate = 2,

        /// <summary>
        /// Already expressed as an hourly cost — no conversion needed.
        /// Typical for: Contractual, Freelance.
        /// </summary>
        HourlyRate = 3,

        /// <summary>
        /// Flat periodic payment (e.g., intern stipend).
        /// Treated identically to MonthlySalary for derivation purposes.
        /// Typical for: Intern.
        /// </summary>
        Stipend = 4
    }
}
