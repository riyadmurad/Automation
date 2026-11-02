using System.Text.Json;
using System.Text.Json.Serialization;
using M365.SecurityAssessment.Api;
using M365.SecurityAssessment.Domain;

var builder = WebApplication.CreateBuilder(args);
var previewEnabled = builder.Configuration.GetValue<bool>("Platform:EnableSyntheticPreview");
SyntheticPreviewPolicy.ValidateConfiguration(builder.Environment.EnvironmentName, previewEnabled);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
