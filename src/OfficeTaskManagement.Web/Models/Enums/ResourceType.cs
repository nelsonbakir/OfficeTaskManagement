namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Classifies how a resource is engaged — drives salary-type defaults
    /// and cost-calculation rules.
    /// </summary>
    public enum ResourceType
    {
        /// <summary>Permanent employee on a monthly/annual salary.</summary>
        FullTime = 0,

        /// <summary>Employee working reduced hours; salary is proportional.</summary>
        PartTime = 1,

        /// <summary>Fixed-term contractor; may be billed daily or hourly.</summary>
        Contractual = 2,

        /// <summary>Per-task or per-hour engagement; no fixed term.</summary>
        Freelance = 3,

        /// <summary>Trainee / intern; may be unpaid or on a flat stipend.</summary>
        Intern = 4,

        /// <summary>External advisory engagement; typically daily or hourly.</summary>
        Consultant = 5
    }
}
