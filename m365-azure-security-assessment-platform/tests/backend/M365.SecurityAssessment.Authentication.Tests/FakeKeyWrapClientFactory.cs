using System.Security.Cryptography;
using M365.SecurityAssessment.Authentication;

namespace M365.SecurityAssessment.Authentication.Tests;

internal sealed class FakeKeyWrapClientFactory : ISessionKeyWrapClientFactory
{
    private readonly Dictionary<string, byte[]> keys = new(StringComparer.Ordinal);

    public void AddKey(string keyId, byte[]? key = null) =>
        keys[keyId] = key ?? RandomNumberGenerator.GetBytes(32);

    public ISessionKeyWrapClient Create(string keyId) =>
        keys.TryGetValue(keyId, out var key)
            ? new FakeKeyWrapClient(key)
            : throw new CryptographicException("Synthetic key version unavailable.");

    private sealed class FakeKeyWrapClient(byte[] key) : ISessionKeyWrapClient
    {
        public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken) =>
            Task.FromResult(Transform(dataKey));

        public Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken cancellationToken) =>
            Task.FromResult(Transform(wrappedDataKey));

        private byte[] Transform(byte[] input)
        {
            var output = new byte[input.Length];
            for (var index = 0; index < input.Length; index++) output[index] = (byte)(input[index] ^ key[index % key.Length]);
            return output;
        }
    }
}