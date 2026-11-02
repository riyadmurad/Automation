using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace M365.SecurityAssessment.Persistence;

internal static class EvidenceObjectKey
{
    public static string Build(CustomerDataScope scope, Guid evidenceId, string sha256) =>
        $"{scope.OrganizationId:D}/{scope.CustomerTenantId:D}/evidence/{evidenceId:D}/{sha256}";
}

internal static class EvidenceContent
{
    public const long MaximumBytes = 64 * 1024 * 1024;

    public static void ValidateRequest(Guid evidenceId, string contentType, string expectedSha256)
    {
        if (evidenceId == Guid.Empty || string.IsNullOrWhiteSpace(contentType) || contentType.Length > 256 ||
            expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Evidence requires an ID, bounded content type, and SHA-256 digest.");
        }
    }

    public static async Task<(string Sha256, long ByteLength)> CopyAndHashAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (length > MaximumBytes)
            {
                throw new InvalidDataException($"Evidence exceeds the {MaximumBytes} byte storage limit.");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), length);
    }

    public static async Task VerifyAndRewindAsync(
        Stream stream,
        string expectedSha256,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException("Evidence must be verified before it is returned to a caller.");
        }

        stream.Position = 0;
        var (actualSha256, actualLength) = await CopyAndHashAsync(stream, Stream.Null, cancellationToken);
        stream.Position = 0;
        if (actualLength != expectedLength || !DigestEquals(actualSha256, expectedSha256))
        {
            throw new InvalidDataException("Evidence is missing, truncated, or does not match its stored integrity digest.");
        }
    }

    public static bool DigestEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static async Task<MemoryStream> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        try
        {
            await CopyAndHashAsync(source, output, cancellationToken);
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }
}

public sealed class LocalEvidenceBlobStore : IEvidenceBlobStore
{
    private readonly string rootDirectory;

    public LocalEvidenceBlobStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A local evidence root directory is required.", nameof(rootDirectory));
        }

        this.rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<EvidenceBlobReference> PutAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        Stream content,
        string contentType,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EvidenceContent.ValidateRequest(evidenceId, contentType, expectedSha256);
        var digest = expectedSha256.ToLowerInvariant();
        var objectPath = GetPath(EvidenceObjectKey.Build(scope, evidenceId, digest));
        var directory = Path.GetDirectoryName(objectPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".pending-{Guid.NewGuid():N}");
        try
        {
            (string Sha256, long ByteLength) copied;
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                copied = await EvidenceContent.CopyAndHashAsync(content, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            if (!EvidenceContent.DigestEquals(copied.Sha256, digest))
            {
                throw new InvalidDataException("Evidence upload did not match the caller-supplied digest.");
            }

            try
            {
                File.Move(temporaryPath, objectPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(objectPath))
            {
                await using var existing = new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await EvidenceContent.VerifyAndRewindAsync(existing, digest, copied.ByteLength, cancellationToken);
            }

            return new EvidenceBlobReference(scope, evidenceId, digest, copied.ByteLength, contentType);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<Stream> OpenVerifiedReadAsync(
        EvidenceBlobReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EvidenceContent.ValidateRequest(reference.EvidenceId, reference.ContentType, reference.Sha256);
        var path = GetPath(EvidenceObjectKey.Build(reference.Scope, reference.EvidenceId, reference.Sha256.ToLowerInvariant()));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await EvidenceContent.VerifyAndRewindAsync(stream, reference.Sha256, reference.ByteLength, cancellationToken);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private string GetPath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(rootDirectory, key.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(rootDirectory, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence object key escaped the configured storage root.");
        }

        return path;
    }
}

public sealed class AzureEvidenceBlobStore(BlobContainerClient containerClient) : IEvidenceBlobStore
{
    public async Task<EvidenceBlobReference> PutAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        Stream content,
        string contentType,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EvidenceContent.ValidateRequest(evidenceId, contentType, expectedSha256);
        await using var bounded = await EvidenceContent.ReadBoundedAsync(content, cancellationToken);
        var (actualSha256, byteLength) = await HashForUploadAsync(bounded, cancellationToken);
        var digest = expectedSha256.ToLowerInvariant();
        if (!EvidenceContent.DigestEquals(actualSha256, digest))
        {
            throw new InvalidDataException("Evidence upload did not match the caller-supplied digest.");
        }

        var blob = containerClient.GetBlobClient(EvidenceObjectKey.Build(scope, evidenceId, digest));
        bounded.Position = 0;
        await blob.UploadAsync(bounded, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sha256"] = digest,
                ["evidenceid"] = evidenceId.ToString("D")
            },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken);

        return new EvidenceBlobReference(scope, evidenceId, digest, byteLength, contentType);
    }

    public async Task<Stream> OpenVerifiedReadAsync(
        EvidenceBlobReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EvidenceContent.ValidateRequest(reference.EvidenceId, reference.ContentType, reference.Sha256);
        var blob = containerClient.GetBlobClient(EvidenceObjectKey.Build(
            reference.Scope, reference.EvidenceId, reference.Sha256.ToLowerInvariant()));
        var downloaded = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        await using var source = downloaded.Value.Content;
        var buffered = await EvidenceContent.ReadBoundedAsync(source, cancellationToken);
        var (actualSha256, actualLength) = await HashForUploadAsync(buffered, cancellationToken);
        if (actualLength != reference.ByteLength || !EvidenceContent.DigestEquals(actualSha256, reference.Sha256))
        {
            await buffered.DisposeAsync();
            throw new InvalidDataException("Azure evidence is missing, truncated, or does not match its stored integrity digest.");
        }

        buffered.Position = 0;
        return buffered;
    }

    private static async Task<(string Sha256, long ByteLength)> HashForUploadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
            hash.AppendData(buffer, 0, read);
        }

        stream.Position = 0;
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), length);
    }
}