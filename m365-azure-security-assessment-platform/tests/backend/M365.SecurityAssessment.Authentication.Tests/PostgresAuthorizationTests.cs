using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;
using Npgsql;
using System.Text.Json;

namespace M365.SecurityAssessment.Authentication.Tests;

[Collection(AuthPostgreSqlCollection.Name)]
public sealed class PostgresAuthorizationTests(AuthPostgreSqlFixture fixture)
{
    [Fact]
    public async Task AssignmentLookupRequiresExactUserTenantAndCustomerAndRechecksRevocation()
    {
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantB);
        await fixture.AddAssignmentAsync(scope, AuthTestData.TenantA, AuthTestData.UserA, ["Assessor"]);
        var repository = new PostgresCustomerAuthorizationRepository(fixture.ApplicationDataSource);

        var assignment = await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserA, scope.OrganizationId, scope.CustomerTenantId);
        var otherUser = await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserB, scope.OrganizationId, scope.CustomerTenantId);
        var otherCustomer = await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserA, scope.OrganizationId, AuthTestData.TenantA);

        Assert.NotNull(assignment);
        Assert.Contains(PlatformRole.Assessor, assignment.Roles);
        Assert.Null(otherUser);
        Assert.Null(otherCustomer);

        await using var connection = await fixture.AdminDataSource.OpenConnectionAsync();
        await using var revoke = new NpgsqlCommand("""
            UPDATE platform.operator_customer_assignments SET is_active = false
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND operator_tenant_id = @operator_tenant_id AND operator_object_id = @operator_object_id
            """, connection);
        revoke.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        revoke.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        revoke.Parameters.AddWithValue("operator_tenant_id", AuthTestData.TenantA);
        revoke.Parameters.AddWithValue("operator_object_id", AuthTestData.UserA);
        await revoke.ExecuteNonQueryAsync();
        Assert.Null(await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserA, scope.OrganizationId, scope.CustomerTenantId));
    }

    [Fact]
    public async Task AssessmentConsentCannotAuthorizeRemediationAndNoCredentialValueIsReturned()
    {
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantB);
        await fixture.AddAssignmentAsync(scope, AuthTestData.TenantA, AuthTestData.UserA, ["Operator"]);
        await fixture.AddConnectionAsync(scope, "assessment", ["Graph.Read.All"]);
        await fixture.AddConnectionAsync(scope, "remediation", ["Graph.Read.All"]);
        var repository = new PostgresCustomerAuthorizationRepository(fixture.ApplicationDataSource);
        var assignment = await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserA, scope.OrganizationId, scope.CustomerTenantId);
        Assert.NotNull(assignment);
        var service = new CustomerConnectionAuthorizationService(repository);

        var read = await service.CheckAsync(assignment, ConnectionPurpose.Assessment, ["Graph.Read.All"]);
        var write = await service.CheckAsync(assignment, ConnectionPurpose.Remediation, ["Graph.Read.All", "Graph.Write.All"]);
        var assessmentConnection = await repository.GetConnectionAsync(assignment, ConnectionPurpose.Assessment);

        Assert.True(read.IsAuthorized);
        Assert.False(write.IsAuthorized);
        Assert.Equal(["Graph.Write.All"], write.MissingScopes);
        Assert.NotNull(assessmentConnection);
        Assert.DoesNotContain("CredentialReference", assessmentConnection.GetType().GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("vault://synthetic-reference-only", JsonSerializer.Serialize(assessmentConnection));

        await fixture.AddAssignmentAsync(scope, AuthTestData.TenantA, AuthTestData.UserB, ["Viewer"]);
        var viewerAssignment = await repository.GetAssignmentAsync(AuthTestData.TenantA, AuthTestData.UserB,
            scope.OrganizationId, scope.CustomerTenantId);
        Assert.NotNull(viewerAssignment);
        Assert.False((await service.CheckAsync(viewerAssignment, ConnectionPurpose.Assessment, ["Graph.Read.All"])).IsAuthorized);

        await using var adminConnection = await fixture.AdminDataSource.OpenConnectionAsync();
        await using var markUnhealthy = new NpgsqlCommand("""
            UPDATE platform.customer_connections SET health = 'degraded'
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id AND purpose = 'assessment'
            """, adminConnection);
        markUnhealthy.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        markUnhealthy.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        await markUnhealthy.ExecuteNonQueryAsync();
        Assert.False((await service.CheckAsync(assignment, ConnectionPurpose.Assessment, ["Graph.Read.All"])).IsAuthorized);

        await using var revokeConnection = await fixture.AdminDataSource.OpenConnectionAsync();
        await using var revoke = new NpgsqlCommand("""
            UPDATE platform.customer_connections SET health = 'healthy', is_active = false
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id AND purpose = 'assessment'
            """, revokeConnection);
        revoke.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        revoke.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        await revoke.ExecuteNonQueryAsync();
        Assert.False((await service.CheckAsync(assignment, ConnectionPurpose.Assessment, ["Graph.Read.All"])).IsAuthorized);
    }
}