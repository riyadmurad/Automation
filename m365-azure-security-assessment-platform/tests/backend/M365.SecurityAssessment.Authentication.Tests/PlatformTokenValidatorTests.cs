using System.Security.Claims;
using System.Security.Cryptography;
using M365.SecurityAssessment.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace M365.SecurityAssessment.Authentication.Tests;

public sealed class PlatformTokenValidatorTests
{
    [Fact]
    public void AcceptsOnlyCurrentTrustedIssuerAudienceTenantAndImmutableActorClaims()
    {
        var options = AuthTestData.Options();
        var now = DateTimeOffset.UtcNow;
        Assert.True(PlatformTokenValidator.HasTrustedClaims(Principal(options, AuthTestData.TenantA,
            "api://m365-assessment", now.AddMinutes(5)), options, now));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(options, AuthTestData.TenantA,
            "api://wrong-audience", now.AddMinutes(5)), options, now));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(options, Guid.NewGuid(),
            "api://m365-assessment", now.AddMinutes(5)), options, now));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(options, AuthTestData.TenantA,
            "api://m365-assessment", now.AddMinutes(-5)), options, now));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(options, AuthTestData.TenantA,
            "api://m365-assessment", now.AddMinutes(5), issuer: "https://attacker.example/issuer"), options, now));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(options, AuthTestData.TenantA,
            "api://m365-assessment", now.AddMinutes(5), includeObjectId: false), options, now));
    }

    [Fact]
    public void StandaloneModeRejectsEveryTenantExceptItsFixedCustomer()
    {
        var defaults = AuthTestData.Options();
        var standalone = new PlatformAuthenticationOptions
        {
            DeploymentMode = PlatformDeploymentMode.Standalone,
            StandaloneCustomerTenantId = AuthTestData.TenantA,
            IssuerBaseUri = defaults.IssuerBaseUri,
            ApiClientId = defaults.ApiClientId,
            ValidAudiences = defaults.ValidAudiences,
            TrustedPlatformTenants = defaults.TrustedPlatformTenants,
            KeyVaultUri = defaults.KeyVaultUri,
            KeyName = defaults.KeyName,
            CurrentKeyVersion = defaults.CurrentKeyVersion,
            KeyVersions = defaults.KeyVersions
        };
        Assert.True(PlatformTokenValidator.HasTrustedClaims(Principal(standalone, AuthTestData.TenantA,
            "api://m365-assessment", DateTimeOffset.UtcNow.AddMinutes(5)), standalone, DateTimeOffset.UtcNow));
        Assert.False(PlatformTokenValidator.HasTrustedClaims(Principal(standalone, AuthTestData.TenantB,
            "api://m365-assessment", DateTimeOffset.UtcNow.AddMinutes(5)), standalone, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RequiresIndependentApproverToDifferFromPlanProposer()
    {
        var assignment = new PlatformCustomerAssignment(
            new M365.SecurityAssessment.Persistence.CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantA),
            AuthTestData.TenantA,
            AuthTestData.UserA,
            new HashSet<PlatformRole> { PlatformRole.Approver },
            true);
        Assert.False(PlatformApprovalAuthorization.CanApprove(assignment, AuthTestData.UserA, AuthTestData.UserA));
        Assert.False(PlatformApprovalAuthorization.CanApprove(assignment, AuthTestData.UserA, AuthTestData.UserB));
        var independentApprover = assignment with { OperatorObjectId = AuthTestData.UserB };
        Assert.True(PlatformApprovalAuthorization.CanApprove(independentApprover, AuthTestData.UserA, AuthTestData.UserB));
    }

    [Fact]
    public void JwtBearerValidationRejectsForgedExpiredWrongIssuerAndWrongAudienceTokens()
    {
        var options = AuthTestData.Options();
        var signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var validation = PlatformTokenValidator.CreateValidationParameters(options);
        validation.ValidateIssuerSigningKey = true;
        validation.IssuerSigningKey = signingKey;
        var handler = new JwtSecurityTokenHandler();
        handler.MapInboundClaims = false;
        var now = DateTime.UtcNow;
        var valid = CreateToken(options.IssuerFor(AuthTestData.TenantA), "api://m365-assessment",
            now.AddMinutes(5), signingKey);

        var principal = handler.ValidateToken(valid, validation, out _);
        Assert.True(PlatformTokenValidator.HasTrustedClaims(principal, options, DateTimeOffset.UtcNow));

        var tokenParts = valid.Split('.');
        tokenParts[2] = (tokenParts[2][0] == 'A' ? "B" : "A") + tokenParts[2][1..];
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(string.Join('.', tokenParts), validation, out _));
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(
            CreateToken("https://attacker.example/issuer", "api://m365-assessment", now.AddMinutes(5), signingKey), validation, out _));
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(
            CreateToken(options.IssuerFor(AuthTestData.TenantA), "api://wrong-audience", now.AddMinutes(5), signingKey), validation, out _));
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(
            CreateToken(options.IssuerFor(AuthTestData.TenantA), "api://m365-assessment", now.AddMinutes(-5), signingKey), validation, out _));
    }

    private static ClaimsPrincipal Principal(
        PlatformAuthenticationOptions options,
        Guid tenantId,
        string audience,
        DateTimeOffset expires,
        string? issuer = null,
        bool includeObjectId = true)
    {
        var claims = new List<Claim>
        {
            new("iss", issuer ?? options.IssuerFor(tenantId)),
            new("tid", tenantId.ToString("D")),
            new("aud", audience),
            new("exp", expires.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (includeObjectId) claims.Add(new Claim("oid", AuthTestData.UserA.ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string CreateToken(string issuer, string audience, DateTime expiresUtc, SymmetricSecurityKey signingKey)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim("oid", AuthTestData.UserA.ToString("D")), new Claim("tid", AuthTestData.TenantA.ToString("D"))],
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}