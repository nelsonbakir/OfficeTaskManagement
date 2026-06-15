namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Indicates how often a <see cref="ProjectOtherCost"/> line item recurs.
    /// <c>OneTime</c> costs are used as-is; recurring costs are annualised
    /// when computing the total cost forecast for the project duration.
    /// </summary>
    public enum CostFrequency
    {
        OneTime   = 1,
        Monthly   = 2,
        Quarterly = 3,
        Annual    = 4
    }
}
