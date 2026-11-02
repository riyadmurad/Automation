using Microsoft.Extensions.Configuration;
using M365.SecurityAssessment.Authentication;

namespace M365.SecurityAssessment.Authentication.Tests;

public sealed class PlatformAuthenticationOptionsTests
{
    [Fact]
    public void RejectsMalformedTenantAllowlistEvenWhenAnotherTrustedTenantIsPresent()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PlatformAuthentication:TrustedPlatformTenants:0"] = AuthTestData.TenantA.ToString("D"),
            ["PlatformAuthentication:TrustedPlatformTenants:1"] = "not-a-tenant-guid"
        });

        Assert.Throws<InvalidOperationException>(() => PlatformAuthenticationOptions.Load(configuration));
    }

    [Fact]
    public void RequiresFixedTenantForStandaloneMode()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PlatformAuthentication:DeploymentMode"] = "Standalone"
        });

        Assert.Throws<InvalidOperationException>(() => PlatformAuthenticationOptions.Load(configuration));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["PlatformAuthentication:DeploymentMode"] = "Msp",
            ["PlatformAuthentication:IssuerBaseUrl"] = "https://login.microsoftonline.com/",
            ["PlatformAuthentication:ApiClientId"] = "api-client",
            ["PlatformAuthentication:ValidAudiences:0"] = "api://m365-assessment",
            ["PlatformAuthentication:TrustedPlatformTenants:0"] = AuthTestData.TenantA.ToString("D"),
            ["PlatformAuthentication:KeyVault:VaultUri"] = "https://auth-test.vault.azure.net/",
            ["PlatformAuthentication:KeyVault:KeyName"] = "operator-session",
            ["PlatformAuthentication:KeyVault:CurrentVersion"] = "1",
            ["PlatformAuthentication:KeyVault:Versions:1"] = "https://auth-test.vault.azure.net/keys/operator-session/version-one"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}