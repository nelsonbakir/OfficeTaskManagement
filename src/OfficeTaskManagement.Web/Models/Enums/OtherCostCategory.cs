namespace OfficeTaskManagement.Models.Enums
{
    /// <summary>
    /// Categorises a non-labour cost line item attached to a project.
    /// Used for budget roll-up reporting and cost-type breakdown charts.
    /// </summary>
    public enum OtherCostCategory
    {
        Hardware     = 1,
        Software     = 2,
        License      = 3,
        Travel       = 4,
        Training     = 5,
        Subcontractor= 6,
        Facilities   = 7,
        Marketing    = 8,
        Legal        = 9,
        Miscellaneous= 10
    }
}
