using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using M365.SecurityAssessment.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace M365.SecurityAssessment.Persistence.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL persistence";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private NpgsqlDataSource? adminDataSource;

    public NpgsqlDataSource ApplicationDataSource { get; private set; } = null!;
    public NpgsqlDataSource AdminDataSource => adminDataSource ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized.");

    public async Task InitializeAsync()
    {
        var applicationPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(36));
        container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithImagePullPolicy(PullPolicy.Never)
            .WithDatabase("m365_persistence_tests")
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

            var migrationPath = Path.Combine(AppContext.BaseDirectory, "migrations", "001_storage_audit.sql");
            var migration = await File.ReadAllTextAsync(migrationPath);
            await using var applyMigration = new NpgsqlCommand(migration, adminConnection);
            await applyMigration.ExecuteNonQueryAsync();
        }

        var applicationConnectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Username = "platform_app",
            Password = applicationPassword,
            MaxPoolSize = 1,
            MinPoolSize = 0
        };
        ApplicationDataSource = NpgsqlDataSource.Create(applicationConnectionString.ConnectionString);
    }

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

    public async Task DisposeAsync()
    {
        await ApplicationDataSource.DisposeAsync();
        if (adminDataSource is not null)
        {
            await adminDataSource.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}