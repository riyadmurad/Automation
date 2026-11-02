using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using M365.SecurityAssessment.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace M365.SecurityAssessment.Authentication.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthPostgreSqlCollection : ICollectionFixture<AuthPostgreSqlFixture>
{
    public const string Name = "Authentication PostgreSQL";
}

public sealed class AuthPostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private NpgsqlDataSource? adminDataSource;

    public NpgsqlDataSource ApplicationDataSource { get; private set; } = null!;
    public NpgsqlDataSource AdminDataSource => adminDataSource ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized.");
    private string ApplicationConnectionString { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var applicationPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(36));
        container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithImagePullPolicy(PullPolicy.Never)
            .WithDatabase("m365_auth_tests")
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(36)))
            .Build();
        await container.StartAsync();

        adminDataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await using (var adminConnection = await adminDataSource.OpenConnectionAsync())
        {
            await using var createRole = new NpgsqlCommand(
                "CREATE ROLE platform_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD " +
                $"'{applicationPassword}'", adminConnection);
            await createRole.ExecuteNonQueryAsync();

            foreach (var migrationName in new[]
            {
                "001_storage_audit.sql",
                "001_platform_authentication.sql",
                "002_bff_auth.sql"
            })
            {
                var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", migrationName));
                await using var applyMigration = new NpgsqlCommand(migration, adminConnection);
                await applyMigration.ExecuteNonQueryAsync();
            }
        }

        var applicationBuilder = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Username = "platform_app",
            Password = applicationPassword,
            MaxPoolSize = 4,
            MinPoolSize = 0
        };
        ApplicationConnectionString = applicationBuilder.ConnectionString;
        ApplicationDataSource = NpgsqlDataSource.Create(ApplicationConnectionString);
    }

    public NpgsqlDataSource CreateApplicationDataSource() => NpgsqlDataSource.Create(ApplicationConnectionString);

    public async Task RegisterScopeAsync(CustomerDataScope scope)
    {
        await using var connection = await AdminDataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.customer_scope (organization_id, customer_tenant_id)
            VALUES (@organization_id, @customer_tenant_id)
            ON CONFLICT DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddAssignmentAsync(
        CustomerDataScope scope,
        Guid operatorTenantId,
        Guid operatorObjectId,
        string[] roles,
        bool requiresIndependentApproval = true)
    {
        await RegisterScopeAsync(scope);
        await using var connection = await AdminDataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.operator_customer_assignments
                (organization_id, customer_tenant_id, operator_tenant_id, operator_object_id, roles, requires_independent_approval)
            VALUES (@organization_id, @customer_tenant_id, @operator_tenant_id, @operator_object_id, @roles, @requires_independent_approval)
            """, connection);
        command.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        command.Parameters.AddWithValue("operator_tenant_id", operatorTenantId);
        command.Parameters.AddWithValue("operator_object_id", operatorObjectId);
        command.Parameters.AddWithValue("roles", roles);
        command.Parameters.AddWithValue("requires_independent_approval", requiresIndependentApproval);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddConnectionAsync(CustomerDataScope scope, string purpose, string[] scopes)
    {
        await RegisterScopeAsync(scope);
        await using var connection = await AdminDataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.customer_connections
                (organization_id, customer_tenant_id, purpose, credential_reference, granted_scopes, health, is_active, last_checked_at_utc)
            VALUES (@organization_id, @customer_tenant_id, @purpose, @credential_reference, @scopes, 'healthy', true, clock_timestamp())
            """, connection);
        command.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
        command.Parameters.AddWithValue("purpose", purpose);
        command.Parameters.AddWithValue("credential_reference", "vault://synthetic-reference-only");
        command.Parameters.AddWithValue("scopes", scopes);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (ApplicationDataSource is not null) await ApplicationDataSource.DisposeAsync();
        if (adminDataSource is not null) await adminDataSource.DisposeAsync();
        if (container is not null) await container.DisposeAsync();
    }
}