using Npgsql;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication;

public sealed class PostgresCustomerAuthorizationRepository(NpgsqlDataSource dataSource)
    : ICustomerAuthorizationRepository
{
    public async Task<PlatformCustomerAssignment?> GetAssignmentAsync(
        Guid operatorTenantId,
        Guid operatorObjectId,
        Guid organizationId,
        Guid customerTenantId,
        CancellationToken cancellationToken = default)
    {
        if (operatorTenantId == Guid.Empty || operatorObjectId == Guid.Empty || organizationId == Guid.Empty || customerTenantId == Guid.Empty)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOperatorContextAsync(connection, transaction, operatorTenantId, operatorObjectId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT organization_id, customer_tenant_id, roles, requires_independent_approval
            FROM platform.operator_customer_assignments
            WHERE operator_tenant_id = @operator_tenant_id
              AND operator_object_id = @operator_object_id
                            AND organization_id = @organization_id
              AND customer_tenant_id = @customer_tenant_id
              AND is_active
            """, connection, transaction);
        command.Parameters.AddWithValue("operator_tenant_id", operatorTenantId);
        command.Parameters.AddWithValue("operator_object_id", operatorObjectId);
        command.Parameters.AddWithValue("organization_id", organizationId);
        command.Parameters.AddWithValue("customer_tenant_id", customerTenantId);
        PlatformCustomerAssignment? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var roles = reader.GetFieldValue<string[]>(2)
                    .Select(role => Enum.TryParse<PlatformRole>(role, true, out var parsed) ? parsed : (PlatformRole?)null)
                    .Where(role => role.HasValue)
                    .Select(role => role!.Value)
                    .ToHashSet();
                result = new PlatformCustomerAssignment(
                    new CustomerDataScope(reader.GetGuid(0), reader.GetGuid(1)),
                    operatorTenantId,
                    operatorObjectId,
                    roles,
                    reader.GetBoolean(3));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CustomerConnectionAuthorization?> GetConnectionAsync(
        PlatformCustomerAssignment assignment,
        ConnectionPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOperatorContextAsync(connection, transaction, assignment.OperatorTenantId, assignment.OperatorObjectId, cancellationToken);
        await SetCustomerScopeAsync(connection, transaction, assignment.Scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT granted_scopes, is_active, health, last_checked_at_utc
            FROM platform.customer_connections
            WHERE organization_id = @organization_id
              AND customer_tenant_id = @customer_tenant_id
              AND purpose = @purpose
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_id", assignment.Scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", assignment.Scope.CustomerTenantId);
        command.Parameters.AddWithValue("purpose", purpose.ToString().ToLowerInvariant());
        CustomerConnectionAuthorization? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                result = new CustomerConnectionAuthorization(
                    assignment.Scope,
                    purpose,
                    reader.GetFieldValue<string[]>(0).ToHashSet(StringComparer.Ordinal),
                    reader.GetBoolean(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task SetOperatorContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT set_config('platform.operator_tenant_id', @tenant_id, true),
                   set_config('platform.operator_object_id', @object_id, true)
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
        command.Parameters.AddWithValue("object_id", objectId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetCustomerScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerDataScope scope,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT set_config('platform.organization_id', @organization_id, true),
                   set_config('platform.customer_tenant_id', @customer_tenant_id, true)
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_id", scope.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}