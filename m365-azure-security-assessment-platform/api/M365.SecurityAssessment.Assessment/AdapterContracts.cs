using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace M365.SecurityAssessment.Assessment;

public enum AdapterApiOrigin
{
    MicrosoftGraph,
    AzureResourceManager,
    DefenderXdr,
    Office365Management,
    PowerPlatform,
    SyntheticLoopback
}

public enum AdapterPaginationKind
{
    None,
    ODataNextLink,
    AzureNextLink
}

public enum AdapterOperationKind
{
    ReadOnly,
    Write
}

public enum AdapterRequestBodyPolicy
{
    None,
    JsonReadQuery
}

public sealed record AdapterOperation(
    string OperationId,
    string HttpMethod,
    string RelativePath,
    string AllowedPathPrefix,
    AdapterOperationKind Kind,
    AdapterRequestBodyPolicy BodyPolicy,
    string ItemsProperty,
    string? ContinuationProperty,
    string? DocumentationUrl,
    string? DocumentationHash,
    bool OperationDocumentationVerified);

public sealed record AdapterManifest(
    string ContractVersion,
    string AdapterId,
    string Service,
    string Version,
    AdapterApiOrigin ApiOrigin,
    string ApiVersion,
    AdapterPaginationKind PaginationKind,
    string Runtime,
    string Cloud,
    string SupportMode,
    bool Enabled,
    bool SyntheticTestOnly,
    string[] PermissionPrerequisites,
    string[] LicensePrerequisites,
    string[] RuntimePrerequisites,
    string UnsupportedReason,
    IReadOnlyList<AdapterOperation> Operations);

public sealed record AdapterCollectionCheckpoint(
    int PagesRetrieved,
    bool IsComplete,
    string? ContinuationUri,
    string Reason);

public sealed record AdapterCollectionResult(
    CollectionOutcome Outcome,
    IReadOnlyList<JsonElement> Items,
    AdapterCollectionCheckpoint Checkpoint,
    HttpStatusCode? LastStatusCode);

public sealed record AssessmentOptions
{
    public string? BaselinePackPath { get; init; }
    public int MaximumPages { get; init; } = 100;
    public int MaximumRetries { get; init; } = 3;
    public int MaximumRetryDelaySeconds { get; init; } = 60;
}

public interface IAssessmentTokenProvider
{
    Task<string> GetAccessTokenAsync(Guid customerTenantId, string resourceScope, CancellationToken cancellationToken);
}

public interface IReadOnlyAdapterClient
{
    Task<AdapterCollectionResult> CollectAsync(
        AdapterManifest manifest,
        string operationId,
        Guid customerTenantId,
        string resourceScope,
        JsonElement? queryBody,
        CancellationToken cancellationToken);
}

public interface IBaselinePackProvider
{
    BaselinePackRelease LoadConfiguredCandidate();
}

public sealed class ConfiguredBaselinePackProvider(
    IOptions<AssessmentOptions> options,
    ImmutableBaselineCatalog catalog) : IBaselinePackProvider
{
    public BaselinePackRelease LoadConfiguredCandidate()
    {
        var path = options.Value.BaselinePackPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AssessmentContractException("Assessment:BaselinePackPath must point to a baseline pack candidate.");
        }

        var pack = BaselinePackJson.Load(path);
        BaselinePackValidator.Validate(pack);
        return catalog.Publish(pack);
    }
}

public static class AdapterManifestValidator
{
    public static void Validate(AdapterManifest manifest, AssessmentOptions? options = null)
    {
        if (manifest.ContractVersion != AssessmentContractVersions.AdapterManifest ||
            string.IsNullOrWhiteSpace(manifest.AdapterId) || string.IsNullOrWhiteSpace(manifest.Service) ||
            string.IsNullOrWhiteSpace(manifest.Version) || !Enum.IsDefined(manifest.ApiOrigin) ||
            !Enum.IsDefined(manifest.PaginationKind) || manifest.Operations.Count > 100)
        {
            throw new AssessmentContractException("Adapter manifest version or required metadata is invalid.");
        }

        if (manifest.Operations.Select(operation => operation.OperationId).Distinct(StringComparer.Ordinal).Count() != manifest.Operations.Count)
        {
            throw new AssessmentContractException("Adapter operation IDs must be unique.");
        }

        if (manifest.Enabled && (manifest.SyntheticTestOnly || manifest.SupportMode != "automated/verified" || manifest.Operations.Count == 0))
        {
            throw new AssessmentContractException("Enabled adapters require verified production support and at least one operation.");
        }

        if (manifest.SupportMode is not ("automated/verified" or "manual/unverified" or "unsupported"))
        {
            throw new AssessmentContractException("Adapter support mode is not recognized.");
        }

        if (manifest.Operations.Count > 0 && !manifest.Enabled && !manifest.SyntheticTestOnly)
        {
            throw new AssessmentContractException("Disabled production manifests cannot carry dormant operations that could be enabled implicitly.");
        }

        foreach (var operation in manifest.Operations)
        {
            ValidateOperation(manifest, operation);
            if (manifest.PaginationKind == AdapterPaginationKind.None && operation.ContinuationProperty is not null ||
                manifest.PaginationKind != AdapterPaginationKind.None && operation.ContinuationProperty is null)
            {
                throw new AssessmentContractException($"Adapter operation '{operation.OperationId}' pagination metadata is inconsistent.");
            }
        }

        if (options is not null &&
            (options.MaximumPages is < 1 or > 1000 || options.MaximumRetries is < 0 or > 5 ||
             options.MaximumRetryDelaySeconds is < 1 or > 900))
        {
            throw new AssessmentContractException("Adapter collection limits are outside their supported bounds.");
        }
    }

    public static Uri GetAuthorizedOrigin(AdapterApiOrigin origin) => origin switch
    {
        AdapterApiOrigin.MicrosoftGraph => new Uri("https://graph.microsoft.com/"),
        AdapterApiOrigin.AzureResourceManager => new Uri("https://management.azure.com/"),
        AdapterApiOrigin.DefenderXdr => new Uri("https://api.security.microsoft.com/"),
        AdapterApiOrigin.Office365Management => new Uri("https://manage.office.com/"),
        AdapterApiOrigin.PowerPlatform => new Uri("https://api.powerplatform.com/"),
        AdapterApiOrigin.SyntheticLoopback => new Uri("http://127.0.0.1/"),
        _ => throw new AssessmentContractException("Adapter API origin is not allowlisted.")
    };

    private static void ValidateOperation(AdapterManifest manifest, AdapterOperation operation)
    {
        var decodedPath = Uri.UnescapeDataString(operation.RelativePath);
        if (string.IsNullOrWhiteSpace(operation.OperationId) || operation.Kind != AdapterOperationKind.ReadOnly ||
            operation.HttpMethod is not ("GET" or "POST") ||
            string.IsNullOrWhiteSpace(operation.RelativePath) || !operation.RelativePath.StartsWith("/", StringComparison.Ordinal) ||
            operation.RelativePath.StartsWith("//", StringComparison.Ordinal) || operation.RelativePath.Contains("..", StringComparison.Ordinal) ||
            decodedPath.Contains('\\', StringComparison.Ordinal) ||
            decodedPath.Split('/').Any(segment => segment is "." or "..") ||
            operation.RelativePath.Contains('?', StringComparison.Ordinal) || operation.RelativePath.Contains('#', StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(operation.AllowedPathPrefix) || !operation.AllowedPathPrefix.StartsWith("/", StringComparison.Ordinal) ||
            !operation.RelativePath.StartsWith(operation.AllowedPathPrefix, StringComparison.Ordinal) ||
            operation.HttpMethod == "GET" && operation.BodyPolicy != AdapterRequestBodyPolicy.None ||
            operation.HttpMethod == "POST" && operation.BodyPolicy != AdapterRequestBodyPolicy.JsonReadQuery)
        {
            throw new AssessmentContractException($"Adapter operation '{operation.OperationId}' is not an allowed read-only operation.");
        }

        if (manifest.Enabled && (!operation.OperationDocumentationVerified ||
            !IsHttps(operation.DocumentationUrl) || !IsSha256(operation.DocumentationHash)))
        {
            throw new AssessmentContractException($"Enabled operation '{operation.OperationId}' lacks verified API documentation provenance.");
        }

        if (manifest.SyntheticTestOnly && manifest.ApiOrigin != AdapterApiOrigin.SyntheticLoopback)
        {
            throw new AssessmentContractException("Synthetic adapter manifests are restricted to loopback origins.");
        }
    }

    private static bool IsHttps(string? value) =>
        value is not null && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

public sealed class ReadOnlyAdapterClient : IReadOnlyAdapterClient
{
    private readonly HttpClient httpClient;
    private readonly IAssessmentTokenProvider tokenProvider;
    private readonly AssessmentOptions options;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public ReadOnlyAdapterClient(
        HttpClient httpClient,
        IAssessmentTokenProvider tokenProvider,
        IOptions<AssessmentOptions> options)
        : this(httpClient, tokenProvider, options.Value, Task.Delay)
    {
    }

    public ReadOnlyAdapterClient(
        HttpClient httpClient,
        IAssessmentTokenProvider tokenProvider,
        AssessmentOptions options,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.httpClient = httpClient;
        this.tokenProvider = tokenProvider;
        this.options = options;
        this.delay = delay ?? Task.Delay;
        AdapterManifestValidator.Validate(new AdapterManifest(
            AssessmentContractVersions.AdapterManifest, "validation", "validation", "1", AdapterApiOrigin.MicrosoftGraph,
            "v1.0", AdapterPaginationKind.None, "unknown", "Global", "manual/unverified", false, false, [], [], [], "", []), options);
    }

    public async Task<AdapterCollectionResult> CollectAsync(
        AdapterManifest manifest,
        string operationId,
        Guid customerTenantId,
        string resourceScope,
        JsonElement? queryBody,
        CancellationToken cancellationToken)
    {
        AdapterManifestValidator.Validate(manifest, options);
        if (customerTenantId == Guid.Empty || string.IsNullOrWhiteSpace(resourceScope))
        {
            throw new AssessmentContractException("Collection requires a customer tenant and token resource scope.");
        }

        if (!manifest.Enabled && !manifest.SyntheticTestOnly)
        {
            throw new AssessmentContractException("This adapter is a visible manual/unsupported gap and cannot collect data.");
        }

        var operation = manifest.Operations.SingleOrDefault(item => item.OperationId == operationId)
            ?? throw new AssessmentContractException("Requested operation is not present in the adapter manifest allowlist.");
        if ((operation.BodyPolicy == AdapterRequestBodyPolicy.None) != (queryBody is null))
        {
            throw new AssessmentContractException("Operation request body does not match the manifest's read-query policy.");
        }

        var apiOrigin = AdapterManifestValidator.GetAuthorizedOrigin(manifest.ApiOrigin);
        var initialUri = new Uri(apiOrigin, operation.RelativePath);
        var accessToken = await tokenProvider.GetAccessTokenAsync(customerTenantId, resourceScope, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new AssessmentContractException("Token provider returned no access token.");
        }

        var items = new List<JsonElement>();
        var nextUri = initialUri;
        var pageCount = 0;
        HttpStatusCode? lastStatus = null;
        while (nextUri is not null && pageCount < options.MaximumPages)
        {
            ValidateContinuationUri(apiOrigin, operation.AllowedPathPrefix, nextUri);
            var response = await SendPageAsync(operation, nextUri, queryBody, accessToken, cancellationToken);
            lastStatus = response.StatusCode;
            if (response.Outcome != CollectionOutcome.Complete)
            {
                var failureOutcome = pageCount == 0 ? response.Outcome : CollectionOutcome.Partial;
                return new AdapterCollectionResult(failureOutcome, items,
                    new AdapterCollectionCheckpoint(pageCount, false, nextUri.AbsoluteUri, response.Reason), lastStatus);
            }

            pageCount++;
            items.AddRange(response.Items);
            nextUri = response.NextUri;
        }

        if (nextUri is not null)
        {
            return new AdapterCollectionResult(CollectionOutcome.Partial, items,
                new AdapterCollectionCheckpoint(pageCount, false, nextUri.AbsoluteUri, "Page limit reached before the source confirmed completion."), lastStatus);
        }

        return new AdapterCollectionResult(CollectionOutcome.Complete, items,
            new AdapterCollectionCheckpoint(pageCount, true, null, "The source returned no continuation link."), lastStatus);
    }

    private async Task<PageResult> SendPageAsync(
        AdapterOperation operation,
        Uri requestUri,
        JsonElement? queryBody,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(operation.HttpMethod), requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (queryBody is not null)
            {
                request.Content = new StringContent(queryBody.Value.GetRawText(), Encoding.UTF8, "application/json");
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (attempt < options.MaximumRetries)
                {
                    await delay(GetRetryDelay(response, attempt), cancellationToken);
                    continue;
                }
                return PageResult.Failed(CollectionOutcome.Throttled, "Retry limit reached after source throttling.", response.StatusCode);
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return PageResult.Failed(CollectionOutcome.Error, "HTTP redirects are not followed by assessment collection.", response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return PageResult.Failed(CollectionOutcome.Denied, "Read authorization was denied.", response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return PageResult.Failed(CollectionOutcome.Error, "Read operation failed; response content was not retained.", response.StatusCode);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty(operation.ItemsProperty, out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    return PageResult.Failed(CollectionOutcome.Partial, "Response did not contain the declared page item array.", response.StatusCode);
                }

                var pageItems = values.EnumerateArray().Select(value => value.Clone()).ToArray();
                Uri? continuation = null;
                if (operation.ContinuationProperty is { } continuationProperty &&
                    document.RootElement.TryGetProperty(continuationProperty, out var link) &&
                    link.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(link.GetString()))
                {
                    var continuationValue = link.GetString()!;
                    if (ContainsPathTraversal(continuationValue))
                    {
                        throw new AssessmentContractException("Pagination link contains an encoded or literal path traversal segment.");
                    }
                    if (!Uri.TryCreate(continuationValue, UriKind.Absolute, out continuation))
                    {
                        return PageResult.Failed(CollectionOutcome.Partial, "Continuation link was not an absolute URI.", response.StatusCode);
                    }
                }

                return PageResult.Succeeded(pageItems, continuation, response.StatusCode);
            }
            catch (JsonException)
            {
                return PageResult.Failed(CollectionOutcome.Partial, "Source response was not valid JSON.", response.StatusCode);
            }
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var maxDelay = TimeSpan.FromSeconds(options.MaximumRetryDelaySeconds);
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta ??
            (retryAfter?.Date is { } retryAt ? retryAt - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        return requested < TimeSpan.Zero ? TimeSpan.Zero : requested > maxDelay ? maxDelay : requested;
    }

    private static void ValidateContinuationUri(Uri authorizedOrigin, string allowedPathPrefix, Uri continuation)
    {
        var decodedPath = Uri.UnescapeDataString(continuation.AbsolutePath);
        var decodedPrefix = Uri.UnescapeDataString(allowedPathPrefix);
        if (!SameOrigin(authorizedOrigin, continuation) || continuation.UserInfo.Length != 0 ||
            !decodedPath.StartsWith(decodedPrefix, StringComparison.Ordinal) || decodedPath.Contains('\\', StringComparison.Ordinal) ||
            decodedPath.Split('/').Any(segment => segment is "." or "..") ||
            continuation.Fragment.Length != 0)
        {
            throw new AssessmentContractException("Pagination link escaped the adapter's authorized origin or path scope.");
        }
    }

    private static bool ContainsPathTraversal(string absoluteUri)
    {
        var authorityStart = absoluteUri.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return true;
        }

        var pathStart = absoluteUri.IndexOf('/', authorityStart + 3);
        if (pathStart < 0)
        {
            return false;
        }

        var pathEnd = absoluteUri.IndexOfAny(['?', '#'], pathStart);
        var path = pathEnd < 0 ? absoluteUri[pathStart..] : absoluteUri[pathStart..pathEnd];
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (path.Contains('\\', StringComparison.Ordinal) || path.Split('/').Any(segment => segment is "." or ".."))
            {
                return true;
            }
            var decoded = Uri.UnescapeDataString(path);
            if (decoded == path)
            {
                return false;
            }
            path = decoded;
        }

        return path.Contains('%', StringComparison.Ordinal);
    }

    private static bool SameOrigin(Uri expected, Uri actual) =>
        expected.Scheme == actual.Scheme && expected.Host.Equals(actual.Host, StringComparison.OrdinalIgnoreCase) && expected.Port == actual.Port;

    private sealed record PageResult(
        CollectionOutcome Outcome,
        IReadOnlyList<JsonElement> Items,
        Uri? NextUri,
        string Reason,
        HttpStatusCode? StatusCode)
    {
        public static PageResult Succeeded(IReadOnlyList<JsonElement> items, Uri? nextUri, HttpStatusCode status) =>
            new(CollectionOutcome.Complete, items, nextUri, "Page collected.", status);

        public static PageResult Failed(CollectionOutcome outcome, string reason, HttpStatusCode status) =>
            new(outcome, [], null, reason, status);
    }
}

public static class InitialAdapterCatalog
{
    private static readonly (string Id, string Service, string Reason)[] ServiceFamilies =
    [
        ("entra", "Entra ID", "Read paths, supported effective-scope evidence, and permission/licensing matrix are not qualified."),
        ("exchange", "Exchange Online", "Supported read operations and recipient-level scope semantics are not qualified."),
        ("teams", "Teams", "Supported read operations and effective policy assignment semantics are not qualified."),
        ("sharepoint", "SharePoint and OneDrive", "Tenant and site-level read paths are not qualified."),
        ("power-platform", "Power Platform and Power Automate", "DLP, environment, and connector-pair read semantics are not qualified."),
        ("intune", "Intune", "Expected inventory provenance and effective deployment read paths are not qualified."),
        ("defender-identity", "Defender for Identity", "Sensor coverage evidence and on-premises runtime are not qualified."),
        ("defender-office", "Defender for Office 365", "Policy precedence, recipient scope, and licensing are not qualified."),
        ("defender-endpoint", "Defender for Endpoint", "Tamper-protection evidence and expected device coverage are not qualified."),
        ("defender-cloud-apps", "Defender for Cloud Apps", "Governance policy-to-app scope and supported read paths are not qualified."),
        ("defender-xdr", "Defender XDR", "Cross-product configuration evidence and licensed product scope are not qualified."),
        ("purview", "Purview", "Audit/DLP read semantics, licensing, and evidence freshness are not qualified."),
        ("azure", "Azure", "ARM inventory completeness and subscription/resource-group scope are not qualified.")
    ];

    public static IReadOnlyList<AdapterManifest> Create() => ServiceFamilies.Select(item => new AdapterManifest(
        AssessmentContractVersions.AdapterManifest,
        $"{item.Id}-assessment",
        item.Service,
        "0.1.0",
        item.Id == "azure" ? AdapterApiOrigin.AzureResourceManager : AdapterApiOrigin.MicrosoftGraph,
        "unverified",
        AdapterPaginationKind.None,
        "Unqualified",
        "Global",
        "manual/unverified",
        false,
        false,
        ["Operation-specific permission not verified."],
        ["Applicable license not verified."],
        ["Runtime and module requirements not verified."],
        item.Reason,
        [])).ToArray();
}

public static class AssessmentServiceCollectionExtensions
{
    public static IServiceCollection AddAssessmentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AssessmentOptions>(configuration.GetSection("Assessment"));
        services.AddSingleton<IAssessmentEvaluator, AssessmentEvaluator>();
        services.AddSingleton<ImmutableBaselineCatalog>();
        services.AddSingleton<IBaselinePackProvider, ConfiguredBaselinePackProvider>();
        services.AddSingleton<IReadOnlyList<AdapterManifest>>(_ => InitialAdapterCatalog.Create());
        services.AddHttpClient<ReadOnlyAdapterClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddTransient<IReadOnlyAdapterClient>(provider => provider.GetRequiredService<ReadOnlyAdapterClient>());
        return services;
    }
}