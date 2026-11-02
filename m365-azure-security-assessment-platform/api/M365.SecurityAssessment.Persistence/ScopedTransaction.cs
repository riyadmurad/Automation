using Npgsql;

namespace M365.SecurityAssessment.Persistence;

internal sealed class ScopedTransaction : IAsyncDisposable
{
    private ScopedTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }

    public static async Task<ScopedTransaction> BeginAsync(
        NpgsqlDataSource dataSource,
        CustomerDataScope scope,
        CancellationToken cancellationToken,
        Guid? userObjectId = null)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT set_config('platform.organization_id', @organization_id, true), " +
                "set_config('platform.customer_tenant_id', @customer_tenant_id, true), " +
                "set_config('platform.user_object_id', @user_object_id, true)",
                connection,
                transaction);
            command.Parameters.AddWithValue("organization_id", scope.OrganizationId.ToString());
            command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId.ToString());
            command.Parameters.AddWithValue("user_object_id", userObjectId?.ToString() ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new ScopedTransaction(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken) => Transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}