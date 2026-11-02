using M365.SecurityAssessment.Authentication;

namespace M365.SecurityAssessment.Authentication.Tests;

internal static class AuthTestData
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    public static readonly Guid UserA = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    public static readonly Guid UserB = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    public static PlatformAuthenticationOptions Options(int currentVersion = 1, IReadOnlyDictionary<int, string>? keyVersions = null) =>
        new()
        {
            DeploymentMode = PlatformDeploymentMode.Msp,
            IssuerBaseUri = new Uri("https://login.microsoftonline.com/"),
            ApiClientId = "api-client",
            ValidAudiences = new HashSet<string>(StringComparer.Ordinal) { "api://m365-assessment" },
            TrustedPlatformTenants = new HashSet<Guid> { TenantA, TenantB },
            KeyVaultUri = new Uri("https://auth-test.vault.azure.net/"),
            KeyName = "operator-session",
            CurrentKeyVersion = currentVersion,
            KeyVersions = keyVersions ?? new Dictionary<int, string>
            {
                [1] = "https://auth-test.vault.azure.net/keys/operator-session/version-one"
            }
        };
}