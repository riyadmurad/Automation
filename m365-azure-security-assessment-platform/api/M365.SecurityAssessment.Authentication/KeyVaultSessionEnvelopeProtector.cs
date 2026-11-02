using System.Security.Cryptography;
using System.Text;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Core;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication;

public interface ISessionKeyWrapClient
{
    Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken);
    Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken cancellationToken);
}

public interface ISessionKeyWrapClientFactory
{
    ISessionKeyWrapClient Create(string keyId);
}

public sealed class KeyVaultSessionKeyWrapClientFactory(TokenCredential credential) : ISessionKeyWrapClientFactory
{
    public ISessionKeyWrapClient Create(string keyId) =>
        new KeyVaultSessionKeyWrapClient(new CryptographyClient(new Uri(keyId), credential));

    private sealed class KeyVaultSessionKeyWrapClient(CryptographyClient client) : ISessionKeyWrapClient
    {
        public async Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
        {
            var result = await client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey, cancellationToken);
            return result.EncryptedKey;
        }

        public async Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken cancellationToken)
        {
            var result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedDataKey, cancellationToken);
            return result.Key;
        }
    }
}

public sealed class KeyVaultSessionEnvelopeProtector : ISessionEnvelopeProtector, ISessionEnvelopeKeyRotator
{
    private const int DataKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly PlatformAuthenticationOptions options;
    private readonly ISessionKeyWrapClientFactory keyWrapClientFactory;

    public KeyVaultSessionEnvelopeProtector(PlatformAuthenticationOptions options, TokenCredential credential)
        : this(options, new KeyVaultSessionKeyWrapClientFactory(credential))
    {
    }

    public KeyVaultSessionEnvelopeProtector(
        PlatformAuthenticationOptions options,
        ISessionKeyWrapClientFactory keyWrapClientFactory)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.keyWrapClientFactory = keyWrapClientFactory ?? throw new ArgumentNullException(nameof(keyWrapClientFactory));
    }

    public async Task<EncryptedSessionEnvelope> ProtectAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        ReadOnlyMemory<byte> plaintext,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateIdentity(userObjectId);
        if (plaintext.IsEmpty || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("Session data and a future UTC expiry are required.");
        }

        var storedExpiry = NormalizeForPostgres(expiresAtUtc);
        if (storedExpiry <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("Session expiry must remain in the future at PostgreSQL timestamp precision.");
        }

        var keyId = options.KeyVersions[options.CurrentKeyVersion];
        var dataKey = RandomNumberGenerator.GetBytes(DataKeyLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var sessionId = Guid.NewGuid();
        var associatedData = BuildAssociatedData(scope, userObjectId, sessionId, storedExpiry);
        try
        {
            using (var aes = new AesGcm(dataKey, TagLength))
            {
                aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, associatedData);
            }

            var keyWrapClient = keyWrapClientFactory.Create(keyId);
            var wrappedKey = await keyWrapClient.WrapAsync(dataKey, cancellationToken);
            return new EncryptedSessionEnvelope(scope, userObjectId, sessionId, ciphertext, nonce, tag,
                wrappedKey, keyId, options.CurrentKeyVersion, storedExpiry);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public async Task<ReadOnlyMemory<byte>> UnprotectAsync(
        EncryptedSessionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateIdentity(envelope.UserObjectId);
        if (envelope.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            envelope.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            envelope.Nonce.Length != NonceLength || envelope.AuthenticationTag.Length != TagLength ||
            envelope.Ciphertext.IsEmpty || envelope.WrappedDataKey.IsEmpty ||
            !options.KeyVersions.TryGetValue(envelope.KeyVersion, out var expectedKeyId) ||
            !string.Equals(expectedKeyId, envelope.KeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("The session envelope is expired, incomplete, or references an unavailable key version.");
        }

        var keyWrapClient = keyWrapClientFactory.Create(expectedKeyId);
        var dataKey = await keyWrapClient.UnwrapAsync(envelope.WrappedDataKey.ToArray(), cancellationToken);
        var plaintext = new byte[envelope.Ciphertext.Length];
        var associatedData = BuildAssociatedData(envelope.Scope, envelope.UserObjectId, envelope.SessionId, envelope.ExpiresAtUtc);
        try
        {
            if (dataKey.Length != DataKeyLength)
            {
                throw new CryptographicException("The session data key has an invalid length.");
            }

            using var aes = new AesGcm(dataKey, TagLength);
            aes.Decrypt(envelope.Nonce.Span, envelope.Ciphertext.Span, envelope.AuthenticationTag.Span,
                plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public async Task<EncryptedSessionEnvelope> RewrapDataKeyAsync(
        EncryptedSessionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !options.KeyVersions.TryGetValue(envelope.KeyVersion, out var oldKeyId) ||
            !string.Equals(oldKeyId, envelope.KeyId, StringComparison.Ordinal) ||
            !options.KeyVersions.TryGetValue(options.CurrentKeyVersion, out var currentKeyId))
        {
            throw new CryptographicException("The session envelope cannot be rewrapped with the configured key versions.");
        }

        var oldKeyClient = keyWrapClientFactory.Create(oldKeyId);
        var dataKey = await oldKeyClient.UnwrapAsync(envelope.WrappedDataKey.ToArray(), cancellationToken);
        try
        {
            if (dataKey.Length != DataKeyLength)
            {
                throw new CryptographicException("The session data key has an invalid length.");
            }

            var currentKeyClient = keyWrapClientFactory.Create(currentKeyId);
            var wrappedDataKey = await currentKeyClient.WrapAsync(dataKey, cancellationToken);
            return envelope with
            {
                WrappedDataKey = wrappedDataKey,
                KeyId = currentKeyId,
                KeyVersion = options.CurrentKeyVersion
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static byte[] BuildAssociatedData(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        DateTimeOffset expiresAtUtc) => Encoding.UTF8.GetBytes(
            $"{scope.OrganizationId:D}|{scope.CustomerTenantId:D}|{userObjectId:D}|{sessionId:D}|{NormalizeForPostgres(expiresAtUtc).UtcTicks}");

        private static DateTimeOffset NormalizeForPostgres(DateTimeOffset value)
        {
            var utcTicks = value.UtcTicks;
            return new DateTimeOffset(utcTicks - utcTicks % 10, TimeSpan.Zero);
        }

    private static void ValidateIdentity(Guid userObjectId)
    {
        if (userObjectId == Guid.Empty)
        {
            throw new ArgumentException("An immutable operator object ID is required.");
        }
    }
}