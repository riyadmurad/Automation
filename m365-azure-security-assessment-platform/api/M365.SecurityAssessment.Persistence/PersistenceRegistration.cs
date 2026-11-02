using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace M365.SecurityAssessment.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPlatformPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("PlatformPersistence") ??
            configuration["PlatformPersistence:ConnectionString"];
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var environmentLabel = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
                ? "development"
                : "non-development";
            throw new InvalidOperationException(
                $"PlatformPersistence connection string is required in {environmentLabel} environments.");
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        var dataSource = dataSourceBuilder.Build();
        services.AddSingleton(dataSource);
        services.AddSingleton<IMetadataRepository, PostgresMetadataRepository>();
        services.AddSingleton<IEncryptedSessionRepository, PostgresEncryptedSessionRepository>();
        services.AddSingleton<IAuditLedgerRepository, PostgresAuditLedgerRepository>();
        services.AddSingleton<IEvidenceRepository, PostgresEvidenceRepository>();
        return services;
    }

    public static IServiceCollection AddLocalEvidenceStore(
        this IServiceCollection services,
        IConfiguration configuration,
        string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        var evidenceMode = configuration["PlatformPersistence:EvidenceMode"];
        if (environment is not ("Development" or "Testing") || evidenceMode != "synthetic-local")
        {
            throw new InvalidOperationException(
                "Local evidence storage requires Development or Testing and explicit synthetic-local mode.");
        }

        services.AddSingleton<IEvidenceBlobStore>(_ => new LocalEvidenceBlobStore(rootDirectory));
        return services;
    }

    public static IServiceCollection AddAzureEvidenceStore(
        this IServiceCollection services,
        BlobContainerClient containerClient)
    {
        ArgumentNullException.ThrowIfNull(containerClient);
        services.AddSingleton<IEvidenceBlobStore>(new AzureEvidenceBlobStore(containerClient));
        return services;
    }
}