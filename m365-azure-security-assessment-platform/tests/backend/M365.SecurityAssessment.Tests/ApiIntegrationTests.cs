using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using M365.SecurityAssessment.Assessment;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task HealthIsAvailableAndSyntheticPreviewIsDisabledByDefault()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/synthetic/fixtures")).StatusCode);
    }

    [Fact]
    public async Task PlatformRoutesFailClosedWhenRequiredConfigurationIsMissing()
    {
        using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var response = await client.GetAsync(
            "/platform/organizations/11111111-1111-1111-1111-111111111111/customers/22222222-2222-2222-2222-222222222222/authorization");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("platform_configuration_incomplete", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConnectionStrings:PlatformPersistence")]
    [InlineData("PlatformAuthentication:ApiClientId")]
    [InlineData("PlatformAuthentication:KeyVault:Versions:1")]
    public async Task PlatformRoutesFailClosedWhenAnyRequiredConfigurationCategoryIsMissing(string missingSetting)
    {
        var partialConfiguration = new Dictionary<string, string?>(PlatformConfiguration, StringComparer.Ordinal);
        partialConfiguration.Remove(missingSetting);
        using var factory = new ApiFactory("Production", platformConfiguration: partialConfiguration);
        using var client = factory.CreateClient();
        var path = $"/platform/organizations/{TestAuthorizationRepository.OrganizationId:D}/customers/{TestAuthorizationRepository.CustomerTenantId:D}/authorization";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public void BaselineCandidateRequiresAnExplicitConfiguredPath()
    {
        using var factory = new ApiFactory("Development");
        var provider = factory.Services.GetRequiredService<IBaselinePackProvider>();

        Assert.Throws<AssessmentContractException>(() => provider.LoadConfiguredCandidate());
    }

    [Fact]
    public async Task CustomerRoutesRequireAuthenticationStoredAssignmentAndPurposeRole()
    {
        using var factory = new ApiFactory("Testing", configuredPlatform: true,
            authorizationRepository: new TestAuthorizationRepository());
        using var client = factory.CreateClient();
        var basePath = $"/platform/organizations/{TestAuthorizationRepository.OrganizationId:D}/customers/{TestAuthorizationRepository.CustomerTenantId:D}";

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"{basePath}/authorization")).StatusCode);

        using var assignedRequest = AuthenticatedRequest(
            $"{basePath}/authorization", TestAuthorizationRepository.AssessorObjectId);
        var assignedResponse = await client.SendAsync(assignedRequest);
        Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);

        using var wrongCustomerRequest = AuthenticatedRequest(
            $"/platform/organizations/{TestAuthorizationRepository.OrganizationId:D}/customers/{Guid.NewGuid():D}/authorization",
            TestAuthorizationRepository.AssessorObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrongCustomerRequest)).StatusCode);

        using var viewerRemediationRequest = AuthenticatedRequest(
            $"{basePath}/connections/remediation", TestAuthorizationRepository.ViewerObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(viewerRemediationRequest)).StatusCode);

        using var assessorRemediationRequest = AuthenticatedRequest(
            $"{basePath}/connections/remediation", TestAuthorizationRepository.AssessorObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(assessorRemediationRequest)).StatusCode);

        using var assessorRequest = AuthenticatedRequest(
            $"{basePath}/connections/assessment", TestAuthorizationRepository.AssessorObjectId);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(assessorRequest)).StatusCode);
    }

    [Fact]
    public async Task EvidenceDownloadUsesTheStoredCustomerAssignmentScope()
    {
        var evidenceRepository = new TestEvidenceRepository();
        using var factory = new ApiFactory("Testing", configuredPlatform: true,
            authorizationRepository: new TestAuthorizationRepository(), evidenceRepository: evidenceRepository);
        using var client = factory.CreateClient();
        var path = $"/platform/organizations/{TestAuthorizationRepository.OrganizationId:D}/customers/{TestAuthorizationRepository.CustomerTenantId:D}/evidence/{Guid.NewGuid():D}";

        using var request = AuthenticatedRequest(path, TestAuthorizationRepository.ViewerObjectId);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("synthetic evidence", await response.Content.ReadAsStringAsync());
        Assert.Equal(TestAuthorizationRepository.OrganizationId, evidenceRepository.LastScope?.OrganizationId);
        Assert.Equal(TestAuthorizationRepository.CustomerTenantId, evidenceRepository.LastScope?.CustomerTenantId);
    }

    [Fact]
    public void ProductionRejectsSyntheticPreviewConfiguration()
    {
        using var factory = new ApiFactory("Production", previewEnabled: true);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task DevelopmentPreviewRequiresExplicitOptInAndLoopbackClient()
    {
        using var factory = new ApiFactory("Development", previewEnabled: true, overrideRemoteAddress: true);
        using var client = factory.CreateClient();

        using var loopbackRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/synthetic/fixtures");
        loopbackRequest.Headers.Add("X-Test-Remote-Address", "127.0.0.1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(loopbackRequest)).StatusCode);

        using var externalRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/synthetic/fixtures");
        externalRequest.Headers.Add("X-Test-Remote-Address", "203.0.113.10");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(externalRequest)).StatusCode);
    }

    private static HttpRequestMessage AuthenticatedRequest(string path, Guid operatorObjectId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Operator-Id", operatorObjectId.ToString("D"));
        request.Headers.Add("X-Test-Operator-Tenant", TestAuthorizationRepository.OperatorTenantId.ToString("D"));
        return request;
    }

    private sealed class ApiFactory(
        string environmentName,
        bool? previewEnabled = null,
        bool overrideRemoteAddress = false,
        bool configuredPlatform = false,
        ICustomerAuthorizationRepository? authorizationRepository = null,
        IEvidenceRepository? evidenceRepository = null,
        IReadOnlyDictionary<string, string?>? platformConfiguration = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            var settings = platformConfiguration ?? (configuredPlatform ? PlatformConfiguration : null);
            if (settings is not null)
            {
                foreach (var setting in settings)
                {
                    builder.UseSetting(setting.Key, setting.Value);
                }
            }

            if (previewEnabled is { } enabled)
            {
                builder.UseSetting("Platform:EnableSyntheticPreview", enabled ? "true" : "false");
            }

            if (overrideRemoteAddress || configuredPlatform)
            {
                builder.ConfigureTestServices(services =>
                {
                    if (overrideRemoteAddress)
                    {
                        services.AddSingleton<IStartupFilter, RemoteAddressStartupFilter>();
                    }

                    if (configuredPlatform)
                    {
                        services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = "Test";
                            options.DefaultChallengeScheme = "Test";
                            options.DefaultForbidScheme = "Test";
                        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });

                        services.RemoveAll<ICustomerAuthorizationRepository>();
                        services.AddSingleton(authorizationRepository ?? new TestAuthorizationRepository());
                        if (evidenceRepository is not null)
                        {
                            services.RemoveAll<IEvidenceRepository>();
                            services.AddSingleton(evidenceRepository);
                        }
                    }
                });
            }
        }
    }

    private static readonly Dictionary<string, string?> PlatformConfiguration = new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:PlatformPersistence"] = "Host=127.0.0.1;Database=platform_test;Username=platform_test",
        ["PlatformAuthentication:DeploymentMode"] = "Msp",
        ["PlatformAuthentication:IssuerBaseUrl"] = "https://login.microsoftonline.com/",
        ["PlatformAuthentication:ApiClientId"] = "test-api-client",
        ["PlatformAuthentication:ValidAudiences:0"] = "api://test-api-client",
        ["PlatformAuthentication:TrustedPlatformTenants:0"] = "33333333-3333-3333-3333-333333333333",
        ["PlatformAuthentication:KeyVault:VaultUri"] = "https://platform-test.vault.azure.net/",
        ["PlatformAuthentication:KeyVault:KeyName"] = "operator-session",
        ["PlatformAuthentication:KeyVault:CurrentVersion"] = "1",
        ["PlatformAuthentication:KeyVault:Versions:1"] = "https://platform-test.vault.azure.net/keys/operator-session/test-version",
        ["PlatformPersistence:EvidenceMode"] = "azure",
        ["PlatformPersistence:EvidenceContainerUri"] = "https://platform-test.blob.core.windows.net/evidence"
    };

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-Operator-Id"], out var objectId) ||
                !Guid.TryParse(Request.Headers["X-Test-Operator-Tenant"], out var tenantId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("oid", objectId.ToString("D")),
                new Claim("tid", tenantId.ToString("D"))
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class TestAuthorizationRepository : ICustomerAuthorizationRepository
    {
        public static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid CustomerTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid OperatorTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid AssessorObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public static readonly Guid ViewerObjectId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        public Task<PlatformCustomerAssignment?> GetAssignmentAsync(
            Guid operatorTenantId,
            Guid operatorObjectId,
            Guid organizationId,
            Guid customerTenantId,
            CancellationToken cancellationToken = default)
        {
            if (operatorTenantId != OperatorTenantId || organizationId != OrganizationId ||
                customerTenantId != CustomerTenantId ||
                operatorObjectId != AssessorObjectId && operatorObjectId != ViewerObjectId)
            {
                return Task.FromResult<PlatformCustomerAssignment?>(null);
            }

            var roles = operatorObjectId == AssessorObjectId
                ? new HashSet<PlatformRole> { PlatformRole.Assessor }
                : new HashSet<PlatformRole> { PlatformRole.Viewer };
            return Task.FromResult<PlatformCustomerAssignment?>(new PlatformCustomerAssignment(
                new CustomerDataScope(OrganizationId, CustomerTenantId), operatorTenantId, operatorObjectId, roles, true));
        }

        public Task<CustomerConnectionAuthorization?> GetConnectionAsync(
            PlatformCustomerAssignment assignment,
            ConnectionPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerConnectionAuthorization?>(new CustomerConnectionAuthorization(
                assignment.Scope, purpose, new HashSet<string>(StringComparer.Ordinal) { "read" }, true,
                "healthy", DateTimeOffset.UtcNow));
    }

    private sealed class TestEvidenceRepository : IEvidenceRepository
    {
        public CustomerDataScope? LastScope { get; private set; }

        public Task<EvidenceBlobReference> IngestAsync(
            CustomerDataScope scope,
            Guid evidenceId,
            Stream content,
            string contentType,
            string expectedSha256,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream> OpenVerifiedReadAsync(
            CustomerDataScope scope,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
        {
            LastScope = scope;
            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes("synthetic evidence"));
            return Task.FromResult(stream);
        }
    }

    private sealed class RemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Address", out var value) &&
                    IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });
            next(application);
        };
    }
}