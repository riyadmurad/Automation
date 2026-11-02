using Npgsql;
using NpgsqlTypes;

namespace M365.SecurityAssessment.Persistence;

public sealed class PostgresEncryptedSessionRepository(NpgsqlDataSource dataSource) : IEncryptedSessionRepository
{
    public async Task StoreAsync(EncryptedSessionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Validate(envelope);
        await using var scoped = await ScopedTransaction.BeginAsync(
            dataSource, envelope.Scope, cancellationToken, envelope.UserObjectId);
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.encrypted_sessions
                (organization_id, customer_tenant_id, user_object_id, session_id, ciphertext, nonce,
                 authentication_tag, wrapped_data_key, key_id, key_version, expires_at_utc)
            VALUES
                (@organization_id, @customer_tenant_id, @user_object_id, @session_id, @ciphertext, @nonce,
                 @authentication_tag, @wrapped_data_key, @key_id, @key_version, @expires_at_utc)
            ON CONFLICT (organization_id, customer_tenant_id, user_object_id, session_id) DO UPDATE SET
                ciphertext = EXCLUDED.ciphertext,
                nonce = EXCLUDED.nonce,
                authentication_tag = EXCLUDED.authentication_tag,
                wrapped_data_key = EXCLUDED.wrapped_data_key,
                key_id = EXCLUDED.key_id,
                key_version = EXCLUDED.key_version,
                expires_at_utc = EXCLUDED.expires_at_utc,
                updated_at_utc = clock_timestamp()
            WHERE platform.encrypted_sessions.revoked_at_utc IS NULL
              AND platform.encrypted_sessions.expires_at_utc > clock_timestamp()
            RETURNING session_id
            """, scoped.Connection, scoped.Transaction);
        AddIdentity(command, envelope.Scope, envelope.UserObjectId, envelope.SessionId);
        command.Parameters.AddWithValue("ciphertext", envelope.Ciphertext.ToArray());
        command.Parameters.AddWithValue("nonce", envelope.Nonce.ToArray());
        command.Parameters.AddWithValue("authentication_tag", envelope.AuthenticationTag.ToArray());
        command.Parameters.AddWithValue("wrapped_data_key", envelope.WrappedDataKey.ToArray());
        command.Parameters.AddWithValue("key_id", envelope.KeyId);
        command.Parameters.AddWithValue("key_version", envelope.KeyVersion);
        command.Parameters.AddWithValue("expires_at_utc", envelope.ExpiresAtUtc);
        var storedId = await command.ExecuteScalarAsync(cancellationToken);
        if (storedId is null)
        {
            throw new InvalidOperationException("Expired or revoked session state cannot be reactivated.");
        }

        await scoped.CommitAsync(cancellationToken);
    }

    public async Task<EncryptedSessionEnvelope?> GetActiveAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(userObjectId, sessionId);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken, userObjectId);
        await using var command = new NpgsqlCommand("""
            SELECT ciphertext, nonce, authentication_tag, wrapped_data_key, key_id, key_version, expires_at_utc
            FROM platform.encrypted_sessions
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND user_object_id = @user_object_id AND session_id = @session_id
              AND revoked_at_utc IS NULL AND expires_at_utc > clock_timestamp()
            """, scoped.Connection, scoped.Transaction);
        AddIdentity(command, scope, userObjectId, sessionId);
        EncryptedSessionEnvelope? envelope = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                envelope = new EncryptedSessionEnvelope(scope, userObjectId, sessionId,
                    reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2),
                    reader.GetFieldValue<byte[]>(3), reader.GetString(4), reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6));
            }
        }

        await scoped.CommitAsync(cancellationToken);
        return envelope;
    }

    public async Task RevokeAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(userObjectId, sessionId);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken, userObjectId);
        await using var command = new NpgsqlCommand("""
            UPDATE platform.encrypted_sessions
            SET revoked_at_utc = COALESCE(revoked_at_utc, clock_timestamp()), updated_at_utc = clock_timestamp()
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND user_object_id = @user_object_id AND session_id = @session_id
            """, scoped.Connection, scoped.Transaction);
        AddIdentity(command, scope, userObjectId, sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await scoped.CommitAsync(cancellationToken);
    }

    private static void AddIdentity(NpgsqlCommand command, CustomerDataScope scope, Guid userObjectId, Guid sessionId)
    {
        PostgresMetadataRepository.AddScope(command, scope);
        command.Parameters.AddWithValue("user_object_id", userObjectId);
        command.Parameters.AddWithValue("session_id", sessionId);
    }

    private static void Validate(EncryptedSessionEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateIdentity(envelope.UserObjectId, envelope.SessionId);
        if (envelope.Ciphertext.IsEmpty || envelope.Nonce.IsEmpty || envelope.AuthenticationTag.IsEmpty ||
            envelope.WrappedDataKey.IsEmpty || string.IsNullOrWhiteSpace(envelope.KeyId) || envelope.KeyVersion <= 0 ||
            envelope.ExpiresAtUtc.Offset != TimeSpan.Zero || envelope.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("Session storage accepts only complete encrypted envelopes with a future UTC expiry.");
        }
    }

    private static void ValidateIdentity(Guid userObjectId, Guid sessionId)
    {
        if (userObjectId == Guid.Empty || sessionId == Guid.Empty)
        {
            throw new ArgumentException("Immutable user and session IDs are required.");
        }
    }
}