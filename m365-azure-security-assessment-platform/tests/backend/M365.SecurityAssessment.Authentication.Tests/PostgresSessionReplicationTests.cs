using System.Text;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication.Tests;

[Collection(AuthPostgreSqlCollection.Name)]
public sealed class PostgresSessionReplicationTests(AuthPostgreSqlFixture fixture)
{
    [Fact]
    public async Task SessionCacheSurvivesTwoIndependentRepositoryInstancesAndRevocationIsOneWay()
    {
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantA);
        await fixture.RegisterScopeAsync(scope);
        var keyFactory = new FakeKeyWrapClientFactory();
        const string keyOne = "https://auth-test.vault.azure.net/keys/operator-session/version-one";
        const string keyTwo = "https://auth-test.vault.azure.net/keys/operator-session/version-two";
        keyFactory.AddKey(keyOne);
        keyFactory.AddKey(keyTwo);
        var oldOptions = AuthTestData.Options();
        var protector = new KeyVaultSessionEnvelopeProtector(oldOptions, keyFactory);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToUniversalTime();
        var envelope = await protector.ProtectAsync(scope, AuthTestData.UserA,
            Encoding.UTF8.GetBytes("synthetic-msal-cache"), expiresAt);
        await using var firstDataSource = fixture.CreateApplicationDataSource();
        await using var secondDataSource = fixture.CreateApplicationDataSource();
        var firstInstance = new PostgresEncryptedSessionRepository(firstDataSource);
        var secondInstance = new PostgresEncryptedSessionRepository(secondDataSource);

        await firstInstance.StoreAsync(envelope);
        var restored = await secondInstance.GetActiveAsync(scope, AuthTestData.UserA, envelope.SessionId);
        Assert.NotNull(restored);
        Assert.Equal("synthetic-msal-cache", Encoding.UTF8.GetString((await protector.UnprotectAsync(restored)).Span));
        Assert.Null(await secondInstance.GetActiveAsync(scope, AuthTestData.UserB, envelope.SessionId));
        var otherTenantScope = new CustomerDataScope(scope.OrganizationId, AuthTestData.TenantB);
        await fixture.RegisterScopeAsync(otherTenantScope);
        Assert.Null(await secondInstance.GetActiveAsync(otherTenantScope, AuthTestData.UserA, envelope.SessionId));

        var rotatedOptions = AuthTestData.Options(2, new Dictionary<int, string> { [1] = keyOne, [2] = keyTwo });
        var rotatedProtector = new KeyVaultSessionEnvelopeProtector(rotatedOptions, keyFactory);
        var rotation = new SessionEnvelopeRotationService(secondInstance, rotatedProtector);
        Assert.True(await rotation.RewrapActiveSessionAsync(scope, AuthTestData.UserA, envelope.SessionId));
        var afterRotation = await firstInstance.GetActiveAsync(scope, AuthTestData.UserA, envelope.SessionId);
        Assert.NotNull(afterRotation);
        Assert.Equal(2, afterRotation.KeyVersion);
        Assert.Equal("synthetic-msal-cache", Encoding.UTF8.GetString((await rotatedProtector.UnprotectAsync(afterRotation)).Span));

        await secondInstance.RevokeAsync(scope, AuthTestData.UserA, envelope.SessionId);
        Assert.Null(await firstInstance.GetActiveAsync(scope, AuthTestData.UserA, envelope.SessionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstInstance.StoreAsync(envelope));
    }
}