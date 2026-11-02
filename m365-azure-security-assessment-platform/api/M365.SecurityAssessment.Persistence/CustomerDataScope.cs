namespace M365.SecurityAssessment.Persistence;

public sealed record CustomerDataScope
{
    public CustomerDataScope(Guid organizationId, Guid customerTenantId)
    {
        if (organizationId == Guid.Empty || customerTenantId == Guid.Empty)
        {
            throw new ArgumentException("Both organization and customer tenant IDs are required.");
        }

        OrganizationId = organizationId;
        CustomerTenantId = customerTenantId;
    }

    public Guid OrganizationId { get; }
    public Guid CustomerTenantId { get; }
}