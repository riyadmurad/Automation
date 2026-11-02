using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using M365.SecurityAssessment.Api;
using M365.SecurityAssessment.Assessment;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Domain;
using M365.SecurityAssessment.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var previewEnabled = builder.Configuration.GetValue<bool>("Platform:EnableSyntheticPreview");
SyntheticPreviewPolicy.ValidateConfiguration(builder.Environment.EnvironmentName, previewEnabled);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddAssessmentServices(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("PlatformPersistence") ??
    builder.Configuration["PlatformPersistence:ConnectionString"];
var platformServicesConfigured = false;
var evidenceStoreConfigured = false;
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPlatformPersistence(builder.Configuration);
    try
    {
        _ = PlatformAuthenticationOptions.Load(builder.Configuration);
        builder.Services.AddPlatformAuthentication(
            builder.Configuration, new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
        platformServicesConfigured = true;
    }
    catch (InvalidOperationException)
    {
        // Platform routes remain unavailable until identity and Key Vault configuration is complete.
    }
    catch (ArgumentException)
    {
        // Malformed identity or Key Vault configuration must not enable platform routes.
    }
}

var evidenceMode = builder.Configuration["PlatformPersistence:EvidenceMode"];
if (string.Equals(evidenceMode, "synthetic-local", StringComparison.Ordinal) &&
    builder.Environment.IsDevelopment() &&
    !string.IsNullOrWhiteSpace(builder.Configuration["PlatformPersistence:LocalEvidenceRoot"]))
{
    builder.Services.AddLocalEvidenceStore(
        builder.Configuration, builder.Configuration["PlatformPersistence:LocalEvidenceRoot"]!);
    evidenceStoreConfigured = true;
}
else if (string.Equals(evidenceMode, "azure", StringComparison.Ordinal) &&
         Uri.TryCreate(builder.Configuration["PlatformPersistence:EvidenceContainerUri"], UriKind.Absolute, out var containerUri) &&
         containerUri.Scheme == Uri.UriSchemeHttps && containerUri.UserInfo.Length == 0 &&
         containerUri.Query.Length == 0 && containerUri.Fragment.Length == 0 &&
         containerUri.AbsolutePath.Trim('/').Length > 0)
{
    builder.Services.AddAzureEvidenceStore(new BlobContainerClient(
        containerUri, new ManagedIdentityCredential(new ManagedIdentityCredentialOptions())));
    evidenceStoreConfigured = true;
}

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/platform") && !platformServicesConfigured)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "platform_configuration_incomplete" });
        return;
    }

    if (context.Request.Path.StartsWithSegments("/platform"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    try
    {
        await next();
    }
    catch (Exception exception) when (exception is NpgsqlException or RequestFailedException)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "platform_dependency_unavailable" });
    }
});

app.UseAuthentication();
app.UseAuthorization();
if (platformServicesConfigured)
{
    app.MapPlatformAuthentication();
    app.MapPlatformDataEndpoints(evidenceStoreConfigured);
}

if (SyntheticPreviewPolicy.IsEnabled(builder.Environment.EnvironmentName, previewEnabled))
{
    var catalog = SyntheticFixtureCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "baselines", "control-inventory.json"),
        Path.Combine(AppContext.BaseDirectory, "synthetic-fixtures"));

    app.MapGet("/api/v1/synthetic/fixtures", (HttpContext context) =>
    {
        if (!SyntheticPreviewPolicy.IsLoopback(context.Connection.RemoteIpAddress))
        {
            return (IResult)Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var summaries = catalog.DescribeFixtures();
        foreach (var summary in summaries)
        {
            ContractVersionValidator.ValidateIngress(summary);
        }

        return Results.Ok(summaries);
    });

    app.MapGet("/api/v1/synthetic/assessments/{tenantId:guid}", (HttpContext context, Guid tenantId) =>
    {
        if (!SyntheticPreviewPolicy.IsLoopback(context.Connection.RemoteIpAddress))
        {
            return (IResult)Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = catalog.Evaluate(tenantId);
        if (result is null)
        {
            return Results.NotFound();
        }

        ContractVersionValidator.ValidateIngress(result);
        return Results.Ok(result);
    });
}

app.MapFallback(() => Results.NotFound(new { error = "No authenticated real-service API is implemented." }));
app.Run();

public partial class Program;
