using Npgsql;

namespace M365.SecurityAssessment.Persistence;

public sealed class PostgresEvidenceRepository(
    NpgsqlDataSource dataSource,
    IEvidenceBlobStore blobStore) : IEvidenceRepository
{
    public async Task<EvidenceBlobReference> IngestAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        Stream content,
        string contentType,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var reference = await blobStore.PutAsync(scope, evidenceId, content, contentType, expectedSha256, cancellationToken);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.evidence_objects
                (organization_id, customer_tenant_id, evidence_id, sha256, byte_length, content_type, storage_key)
            VALUES
                (@organization_id, @customer_tenant_id, @evidence_id, @sha256, @byte_length, @content_type, @storage_key)
            """, scoped.Connection, scoped.Transaction);
        PostgresMetadataRepository.AddScope(command, scope);
        command.Parameters.AddWithValue("evidence_id", evidenceId);
        command.Parameters.AddWithValue("sha256", reference.Sha256);
        command.Parameters.AddWithValue("byte_length", reference.ByteLength);
        command.Parameters.AddWithValue("content_type", reference.ContentType);
        command.Parameters.AddWithValue("storage_key", EvidenceObjectKey.Build(scope, evidenceId, reference.Sha256));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await scoped.CommitAsync(cancellationToken);
        return reference;
    }

    public async Task<Stream> OpenVerifiedReadAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        if (evidenceId == Guid.Empty)
        {
            throw new ArgumentException("An evidence ID is required.", nameof(evidenceId));
        }

        EvidenceBlobReference reference;
        await using (var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken))
        await using (var command = new NpgsqlCommand("""
            SELECT sha256, byte_length, content_type, storage_key
            FROM platform.evidence_objects
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND evidence_id = @evidence_id
            """, scoped.Connection, scoped.Transaction))
        {
            PostgresMetadataRepository.AddScope(command, scope);
            command.Parameters.AddWithValue("evidence_id", evidenceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new KeyNotFoundException("Evidence is not available in the requested customer scope.");
            }

            var sha256 = reader.GetString(0);
            var length = reader.GetInt64(1);
            var contentType = reader.GetString(2);
            var storageKey = reader.GetString(3);
            if (storageKey != EvidenceObjectKey.Build(scope, evidenceId, sha256))
            {
                throw new InvalidDataException("Evidence metadata does not match its tenant-scoped storage key.");
            }

            reference = new EvidenceBlobReference(scope, evidenceId, sha256, length, contentType);
            await reader.DisposeAsync();
            await scoped.CommitAsync(cancellationToken);
        }

        return await blobStore.OpenVerifiedReadAsync(reference, cancellationToken);
    }
}