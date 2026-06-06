namespace OfficeTaskManagement.Services
{
    public interface ITenantProvider
    {
        string TenantId { get; }
        void SetTenant(string tenantId);
    }
}
