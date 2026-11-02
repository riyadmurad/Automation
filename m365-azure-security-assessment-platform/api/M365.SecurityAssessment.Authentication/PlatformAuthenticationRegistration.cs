using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication;

public static class PlatformAuthenticationRegistration
{
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        TokenCredential? keyVaultCredential = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = PlatformAuthenticationOptions.Load(configuration);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(Npgsql.NpgsqlDataSource)) ||
            !services.Any(descriptor => descriptor.ServiceType == typeof(IEncryptedSessionRepository)))
        {
            throw new InvalidOperationException("Register AddPlatformPersistence before AddPlatformAuthentication.");
        }

        services.AddSingleton(options);
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        var effectiveCredential = keyVaultCredential ??
            (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase)
            ? new DefaultAzureCredential()
                : new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
        services.AddSingleton(effectiveCredential);
        services.AddSingleton<ISessionEnvelopeProtector>(provider => new KeyVaultSessionEnvelopeProtector(
            options,
            new KeyVaultSessionKeyWrapClientFactory(provider.GetRequiredService<TokenCredential>())));
        services.AddSingleton<ISessionEnvelopeKeyRotator>(provider =>
            (ISessionEnvelopeKeyRotator)provider.GetRequiredService<ISessionEnvelopeProtector>());
        services.AddScoped<ICustomerAuthorizationRepository, PostgresCustomerAuthorizationRepository>();
        services.AddScoped<CustomerConnectionAuthorizationService>();
        services.AddScoped<SessionEnvelopeRotationService>();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, CustomerRoleAuthorizationHandler>();

        var identityConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Instance"] = options.IssuerBaseUri.AbsoluteUri,
                ["TenantId"] = "organizations",
                ["ClientId"] = options.ApiClientId,
                ["Audience"] = options.ValidAudiences.First()
            })
            .Build();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(identityConfiguration, JwtBearerDefaults.AuthenticationScheme);
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = PlatformTokenValidator.CreateValidationParameters(options);
            jwt.TokenValidationParameters.ValidAudience = null;
            jwt.Events ??= new JwtBearerEvents();
            var previousTokenValidated = jwt.Events.OnTokenValidated;
            jwt.Events.OnTokenValidated = async context =>
            {
                if (previousTokenValidated is not null)
                {
                    await previousTokenValidated(context);
                }

                if (context.Principal?.Identity?.IsAuthenticated != true ||
                    !PlatformTokenValidator.HasTrustedClaims(context.Principal, options, DateTimeOffset.UtcNow))
                {
                    context.Fail("The token is not from an assigned trusted platform tenant.");
                }
            };
        });

        var viewerRoles = new HashSet<PlatformRole>(Enum.GetValues<PlatformRole>());
        var assessorRoles = new HashSet<PlatformRole> { PlatformRole.Assessor, PlatformRole.Operator, PlatformRole.Admin };
        var operatorRoles = new HashSet<PlatformRole> { PlatformRole.Operator, PlatformRole.Admin };
        var approverRoles = new HashSet<PlatformRole> { PlatformRole.Approver, PlatformRole.Admin };
        services.AddAuthorizationBuilder()
            .AddPolicy("Platform.Viewer", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerRoleRequirement(viewerRoles)))
            .AddPolicy("Platform.Assessor", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerRoleRequirement(assessorRoles)))
            .AddPolicy("Platform.Approver", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerRoleRequirement(approverRoles)))
            .AddPolicy("Platform.Operator", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerRoleRequirement(operatorRoles)))
            .AddPolicy("Platform.Admin", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerRoleRequirement(new HashSet<PlatformRole> { PlatformRole.Admin })));
        return services;
    }

    public static IApplicationBuilder UsePlatformAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static IEndpointRouteBuilder MapPlatformAuthentication(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/platform/organizations/{organizationId:guid}/customers/{customerTenantId:guid}");
        group.MapGet("/authorization", (HttpContext context) =>
        {
            var assignment = PlatformAuthorizationContext.GetAssignment(context);
            return assignment is null
                ? Results.Forbid()
                : Results.Ok(new
                {
                    assignment.Scope.OrganizationId,
                    assignment.Scope.CustomerTenantId,
                    Roles = assignment.Roles.Select(role => role.ToString()).Order(StringComparer.Ordinal),
                    assignment.RequiresIndependentApproval
                });
        })
            .RequireAuthorization("Platform.Viewer");
        group.MapGet("/connections/assessment", GetAssessmentConnection)
            .RequireAuthorization("Platform.Assessor");
        group.MapGet("/connections/remediation", GetRemediationConnection)
            .RequireAuthorization("Platform.Operator");
        return endpoints;
    }

    private static Task<IResult> GetAssessmentConnection(
        HttpContext context,
        CustomerConnectionAuthorizationService service) =>
        GetConnection(context, service, ConnectionPurpose.Assessment);

    private static Task<IResult> GetRemediationConnection(
        HttpContext context,
        CustomerConnectionAuthorizationService service) =>
        GetConnection(context, service, ConnectionPurpose.Remediation);

    private static async Task<IResult> GetConnection(
        HttpContext context,
        CustomerConnectionAuthorizationService service,
        ConnectionPurpose purpose)
    {
        var assignment = PlatformAuthorizationContext.GetAssignment(context);
        if (assignment is null) return Results.Forbid();
        var connection = await service.GetStatusAsync(assignment, purpose, context.RequestAborted);
        return connection is null
            ? Results.NotFound(new { reason = "connection_not_configured" })
            : Results.Ok(new
            {
                connection.Purpose,
                connection.GrantedScopes,
                connection.IsActive,
                connection.Health,
                connection.LastCheckedAtUtc
            });
    }
}

public static class PlatformTokenValidator
{
    public static TokenValidationParameters CreateValidationParameters(PlatformAuthenticationOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuers = options.TrustedPlatformTenants.Select(options.IssuerFor).ToArray(),
        ValidateAudience = true,
        ValidAudiences = options.ValidAudiences,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    public static bool HasTrustedClaims(
        System.Security.Claims.ClaimsPrincipal principal,
        PlatformAuthenticationOptions options,
        DateTimeOffset nowUtc)
    {
        var oidText = principal.FindFirst("oid")?.Value;
        var tidText = principal.FindFirst("tid")?.Value;
        var issuer = principal.FindFirst("iss")?.Value;
        var expirationText = principal.FindFirst("exp")?.Value;
        if (!Guid.TryParse(oidText, out var objectId) || objectId == Guid.Empty ||
            !Guid.TryParse(tidText, out var tenantId) || !options.TrustedPlatformTenants.Contains(tenantId) ||
            !long.TryParse(expirationText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var expiration) ||
            expiration <= nowUtc.ToUnixTimeSeconds() ||
            (options.DeploymentMode == PlatformDeploymentMode.Standalone && options.StandaloneCustomerTenantId != tenantId) ||
            !string.Equals(issuer, options.IssuerFor(tenantId), StringComparison.Ordinal))
        {
            return false;
        }

        return principal.FindAll("aud").Any(claim => options.ValidAudiences.Contains(claim.Value));
    }
}