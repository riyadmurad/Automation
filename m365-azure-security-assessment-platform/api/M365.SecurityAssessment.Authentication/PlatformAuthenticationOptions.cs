using Microsoft.Extensions.Configuration;

namespace M365.SecurityAssessment.Authentication;

public enum PlatformDeploymentMode
{
    Msp,
    Standalone
}

public sealed class PlatformAuthenticationOptions
{
    public required PlatformDeploymentMode DeploymentMode { get; init; }
    public required Uri IssuerBaseUri { get; init; }
    public required string ApiClientId { get; init; }
    public required IReadOnlySet<string> ValidAudiences { get; init; }
    public required IReadOnlySet<Guid> TrustedPlatformTenants { get; init; }
    public Guid? StandaloneCustomerTenantId { get; init; }
    public required Uri KeyVaultUri { get; init; }
    public required string KeyName { get; init; }
    public required int CurrentKeyVersion { get; init; }
    public required IReadOnlyDictionary<int, string> KeyVersions { get; init; }

    public static PlatformAuthenticationOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("PlatformAuthentication");
        var modeText = section["DeploymentMode"];
        if (!Enum.TryParse<PlatformDeploymentMode>(modeText, true, out var mode))
        {
            throw new InvalidOperationException("PlatformAuthentication:DeploymentMode must be Msp or Standalone.");
        }

        var issuerText = section["IssuerBaseUrl"];
        if (!Uri.TryCreate(issuerText, UriKind.Absolute, out var issuerBase) || issuerBase.Scheme != Uri.UriSchemeHttps ||
            issuerBase.UserInfo.Length != 0 || issuerBase.AbsolutePath != "/" ||
            issuerBase.Query.Length != 0 || issuerBase.Fragment.Length != 0)
        {
            throw new InvalidOperationException("A trusted HTTPS PlatformAuthentication:IssuerBaseUrl is required.");
        }

        var clientId = Required(section["ApiClientId"], "PlatformAuthentication:ApiClientId");
        var audiences = section.GetSection("ValidAudiences").GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
        if (audiences.Count == 0)
        {
            throw new InvalidOperationException("At least one PlatformAuthentication:ValidAudiences value is required.");
        }

        var tenantValues = section.GetSection("TrustedPlatformTenants").GetChildren()
            .Select(child => child.Value)
            .ToArray();
        if (tenantValues.Length == 0 || tenantValues.Any(value =>
                !Guid.TryParse(value, out var tenantId) || tenantId == Guid.Empty))
        {
            throw new InvalidOperationException("Trusted platform tenants must contain only non-empty tenant GUIDs.");
        }
        var tenants = tenantValues.Select(value => Guid.Parse(value!)).ToHashSet();

        Guid? standaloneTenantId = null;
        if (mode == PlatformDeploymentMode.Standalone)
        {
            if (!Guid.TryParse(section["StandaloneCustomerTenantId"], out var configuredTenant) || configuredTenant == Guid.Empty)
            {
                throw new InvalidOperationException("Standalone mode requires a fixed StandaloneCustomerTenantId.");
            }

            standaloneTenantId = configuredTenant;
        }

        var keyVault = section.GetSection("KeyVault");
        if (!Uri.TryCreate(keyVault["VaultUri"], UriKind.Absolute, out var vaultUri) || vaultUri.Scheme != Uri.UriSchemeHttps ||
            vaultUri.UserInfo.Length != 0 || vaultUri.AbsolutePath != "/" || vaultUri.Query.Length != 0 || vaultUri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("A trusted HTTPS PlatformAuthentication:KeyVault:VaultUri is required.");
        }

        var keyName = Required(keyVault["KeyName"], "PlatformAuthentication:KeyVault:KeyName");
        if (!int.TryParse(keyVault["CurrentVersion"], out var currentVersion) || currentVersion < 1)
        {
            throw new InvalidOperationException("PlatformAuthentication:KeyVault:CurrentVersion must be positive.");
        }

        var keyVersions = new Dictionary<int, string>();
        foreach (var child in keyVault.GetSection("Versions").GetChildren())
        {
            if (int.TryParse(child.Key, out var version) && version > 0 && !string.IsNullOrWhiteSpace(child.Value))
            {
                keyVersions.Add(version, child.Value);
            }
        }

        if (!keyVersions.TryGetValue(currentVersion, out _) ||
            keyVersions.Keys.Any(version => version < 1 || version > currentVersion) ||
            keyVersions.Values.Any(keyId => !IsExpectedKeyId(keyId, vaultUri, keyName)))
        {
            throw new InvalidOperationException("KeyVault:Versions must map accepted positive versions to exact versioned key IDs for the configured vault and key name.");
        }

        return new PlatformAuthenticationOptions
        {
            DeploymentMode = mode,
            IssuerBaseUri = issuerBase,
            ApiClientId = clientId,
            ValidAudiences = audiences,
            TrustedPlatformTenants = tenants,
            StandaloneCustomerTenantId = standaloneTenantId,
            KeyVaultUri = vaultUri,
            KeyName = keyName,
            CurrentKeyVersion = currentVersion,
            KeyVersions = keyVersions
        };
    }

    public string IssuerFor(Guid tenantId) =>
        $"{IssuerBaseUri.AbsoluteUri.TrimEnd('/')}/{tenantId:D}/v2.0";

    private static bool IsExpectedKeyId(string value, Uri vaultUri, string keyName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var keyId) || keyId.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(keyId.GetLeftPart(UriPartial.Authority), vaultUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = keyId.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 && segments[0] == "keys" && segments[1] == keyName &&
            segments[2].Length > 0 && keyId.Query.Length == 0 && keyId.Fragment.Length == 0;
    }

    private static string Required(string? value, string settingName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{settingName} is required.")
            : value.Trim();
}