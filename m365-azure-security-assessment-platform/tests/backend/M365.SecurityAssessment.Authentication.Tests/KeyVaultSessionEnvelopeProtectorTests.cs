using System.Security.Cryptography;
using System.Text;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication.Tests;

public sealed class KeyVaultSessionEnvelopeProtectorTests
{
    private const string KeyOne = "https://auth-test.vault.azure.net/keys/operator-session/version-one";
    private const string KeyTwo = "https://auth-test.vault.azure.net/keys/operator-session/version-two";

    [Fact]
    public async Task ProtectsSessionWithAesGcmAndBindsItToUserTenantAndScope()
    {
        var keyFactory = new FakeKeyWrapClientFactory();
        keyFactory.AddKey(KeyOne);
        var protector = new KeyVaultSessionEnvelopeProtector(AuthTestData.Options(), keyFactory);
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantA);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUniversalTime();
        var envelope = await protector.ProtectAsync(scope, AuthTestData.UserA, Encoding.UTF8.GetBytes("synthetic-msal-cache"), expiry);

        var plaintext = await protector.UnprotectAsync(envelope);
        Assert.Equal("synthetic-msal-cache", Encoding.UTF8.GetString(plaintext.Span));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => protector.UnprotectAsync(envelope with { UserObjectId = AuthTestData.UserB }));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => protector.UnprotectAsync(
            envelope with { Scope = new CustomerDataScope(scope.OrganizationId, AuthTestData.TenantB) }));
    }

    [Fact]
    public async Task RetainsOldKeyVersionsForRotationAndRejectsUnavailableVersions()
    {
        var keyFactory = new FakeKeyWrapClientFactory();
        keyFactory.AddKey(KeyOne);
        keyFactory.AddKey(KeyTwo);
        var oldOptions = AuthTestData.Options();
        var oldProtector = new KeyVaultSessionEnvelopeProtector(oldOptions, keyFactory);
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantA);
        var envelope = await oldProtector.ProtectAsync(scope, AuthTestData.UserA, "synthetic"u8.ToArray(),
            DateTimeOffset.UtcNow.AddMinutes(5).ToUniversalTime());

        var rotated = AuthTestData.Options(2, new Dictionary<int, string> { [1] = KeyOne, [2] = KeyTwo });
        var rotatedProtector = new KeyVaultSessionEnvelopeProtector(rotated, keyFactory);
        Assert.Equal("synthetic", Encoding.UTF8.GetString((await rotatedProtector.UnprotectAsync(envelope)).Span));

        var retired = AuthTestData.Options(2, new Dictionary<int, string> { [2] = KeyTwo });
        var retiredProtector = new KeyVaultSessionEnvelopeProtector(retired, keyFactory);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => retiredProtector.UnprotectAsync(envelope));
    }

    [Fact]
    public async Task MissingVaultKeyAndExpiredEnvelopeFailClosed()
    {
        var missingKeyProtector = new KeyVaultSessionEnvelopeProtector(AuthTestData.Options(), new FakeKeyWrapClientFactory());
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantA);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => missingKeyProtector.ProtectAsync(
            scope, AuthTestData.UserA, "synthetic"u8.ToArray(), DateTimeOffset.UtcNow.AddMinutes(5).ToUniversalTime()));

        var available = new FakeKeyWrapClientFactory();
        available.AddKey(KeyOne);
        var protector = new KeyVaultSessionEnvelopeProtector(AuthTestData.Options(), available);
        var envelope = await protector.ProtectAsync(scope, AuthTestData.UserA, "synthetic"u8.ToArray(),
            DateTimeOffset.UtcNow.AddMinutes(5).ToUniversalTime());
        await Assert.ThrowsAnyAsync<CryptographicException>(() => protector.UnprotectAsync(
            envelope with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToUniversalTime() }));
    }
}