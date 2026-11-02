using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using M365.SecurityAssessment.Domain;
using Npgsql;
using NpgsqlTypes;

namespace M365.SecurityAssessment.Persistence;

public sealed class PostgresAuditLedgerRepository(NpgsqlDataSource dataSource) : IAuditLedgerRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task AppendAsync(SettingAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ValidateEvent(auditEvent);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, auditEvent.Scope, cancellationToken);

        var lockKey = $"{auditEvent.Scope.OrganizationId:D}:{auditEvent.Scope.CustomerTenantId:D}";
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0))", scoped.Connection, scoped.Transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_key", lockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        DateTimeOffset occurredAtUtc;
        await using (var clockCommand = new NpgsqlCommand("SELECT clock_timestamp()", scoped.Connection, scoped.Transaction))
        {
            occurredAtUtc = await clockCommand.ExecuteScalarAsync(cancellationToken) is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : throw new InvalidOperationException("PostgreSQL did not return an audit timestamp.");
        }

        string previousHash;
        await using (var previousCommand = new NpgsqlCommand("""
            SELECT event_hash FROM platform.audit_events
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
            ORDER BY ledger_sequence DESC LIMIT 1
            """, scoped.Connection, scoped.Transaction))
        {
            PostgresMetadataRepository.AddScope(previousCommand, auditEvent.Scope);
            previousHash = (string?)await previousCommand.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
        }

        var persisted = Redact(auditEvent with { OccurredAtUtc = occurredAtUtc });
        var canonicalPayload = JsonSerializer.Serialize(persisted, JsonOptions);
        var eventHash = ComputeHash(previousHash, canonicalPayload);
        await using (var insertCommand = new NpgsqlCommand("""
            INSERT INTO platform.audit_events
                (organization_id, customer_tenant_id, event_id, occurred_at_utc, previous_hash, canonical_payload, event_hash)
            VALUES
                (@organization_id, @customer_tenant_id, @event_id, @occurred_at_utc, @previous_hash, @canonical_payload, @event_hash)
            """, scoped.Connection, scoped.Transaction))
        {
            PostgresMetadataRepository.AddScope(insertCommand, persisted.Scope);
            insertCommand.Parameters.AddWithValue("event_id", persisted.EventId);
            insertCommand.Parameters.AddWithValue("occurred_at_utc", persisted.OccurredAtUtc);
            insertCommand.Parameters.AddWithValue("previous_hash", previousHash);
            insertCommand.Parameters.AddWithValue("canonical_payload", canonicalPayload);
            insertCommand.Parameters.AddWithValue("event_hash", eventHash);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await scoped.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SettingAuditEvent>> QueryAsync(
        CustomerDataScope scope,
        AuditQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT canonical_payload
            FROM platform.audit_events
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND (@from_utc IS NULL OR occurred_at_utc >= @from_utc)
              AND (@to_utc IS NULL OR occurred_at_utc <= @to_utc)
              AND (@before_utc IS NULL OR occurred_at_utc < @before_utc)
              AND (@authorized_user_id IS NULL OR
                   canonical_payload::jsonb #>> '{requester,subjectId}' = @authorized_user_id OR
                   canonical_payload::jsonb #>> '{approver,subjectId}' = @authorized_user_id OR
                   canonical_payload::jsonb #>> '{executor,subjectId}' = @authorized_user_id)
              AND (@service_name IS NULL OR canonical_payload::jsonb ->> 'serviceName' = @service_name)
              AND (@setting_name IS NULL OR canonical_payload::jsonb ->> 'settingName' = @setting_name)
              AND (@resource_id IS NULL OR canonical_payload::jsonb ->> 'resourceId' = @resource_id)
              AND (@outcome IS NULL OR canonical_payload::jsonb #>> '{outcome}' = @outcome)
            ORDER BY ledger_sequence DESC LIMIT @limit
            """, scoped.Connection, scoped.Transaction);
        PostgresMetadataRepository.AddScope(command, scope);
        AddNullable(command, "from_utc", NpgsqlDbType.TimestampTz, filter.FromUtc);
        AddNullable(command, "to_utc", NpgsqlDbType.TimestampTz, filter.ToUtc);
        AddNullable(command, "before_utc", NpgsqlDbType.TimestampTz, filter.BeforeUtc);
        AddNullable(command, "authorized_user_id", NpgsqlDbType.Text, filter.AuthorizedUserId);
        AddNullable(command, "service_name", NpgsqlDbType.Text, filter.ServiceName);
        AddNullable(command, "setting_name", NpgsqlDbType.Text, filter.SettingName);
        AddNullable(command, "resource_id", NpgsqlDbType.Text, filter.ResourceId);
        AddNullable(command, "outcome", NpgsqlDbType.Text,
            filter.Outcome is null ? null : JsonNamingPolicy.CamelCase.ConvertName(filter.Outcome.Value.ToString()));
        command.Parameters.AddWithValue("limit", filter.Limit);

        var events = new List<SettingAuditEvent>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(JsonSerializer.Deserialize<SettingAuditEvent>(reader.GetString(0), JsonOptions)
                    ?? throw new InvalidOperationException("An audit event could not be deserialized."));
            }
        }

        await scoped.CommitAsync(cancellationToken);
        return events;
    }

    public async Task<AuditIntegrityResult> VerifyIntegrityAsync(
        CustomerDataScope scope,
        CancellationToken cancellationToken = default)
    {
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT event_id, occurred_at_utc, previous_hash, canonical_payload, event_hash
            FROM platform.audit_events
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
            ORDER BY ledger_sequence ASC
            """, scoped.Connection, scoped.Transaction);
        PostgresMetadataRepository.AddScope(command, scope);

        var previousHash = string.Empty;
        var eventCount = 0;
        Guid? firstInvalid = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                eventCount++;
                var eventId = reader.GetGuid(0);
                 var storedTimestamp = reader.GetFieldValue<DateTimeOffset>(1);
                 var storedPreviousHash = reader.GetString(2);
                 var canonicalPayload = reader.GetString(3);
                 var storedHash = reader.GetString(4);
                var expectedHash = ComputeHash(previousHash, canonicalPayload);
                var payload = JsonSerializer.Deserialize<SettingAuditEvent>(canonicalPayload, JsonOptions);
                if (firstInvalid is null &&
                    (storedPreviousHash != previousHash || storedHash != expectedHash || payload?.EventId != eventId ||
                     payload.Scope != scope || payload.OccurredAtUtc != storedTimestamp))
                {
                    firstInvalid = eventId;
                }

                previousHash = storedHash;
            }
        }

        await scoped.CommitAsync(cancellationToken);
        return new AuditIntegrityResult(firstInvalid is null, eventCount, firstInvalid);
    }

    internal static string ComputeHash(string previousHash, string canonicalPayload)
    {
        var bytes = Encoding.UTF8.GetBytes(previousHash + "\n" + canonicalPayload);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static SettingAuditEvent Redact(SettingAuditEvent value) => value with
    {
        Requester = RedactActor(value.Requester),
        Approver = value.Approver is null ? null : RedactActor(value.Approver),
        Executor = RedactActor(value.Executor),
        PreviousValue = RedactValue(value.SettingName, value.PreviousValue),
        RequestedValue = RedactValue(value.SettingName, value.RequestedValue),
        VerifiedValue = RedactValue(value.SettingName, value.VerifiedValue)
    };

    private static TrustedAuditActor RedactActor(TrustedAuditActor actor)
    {
        if (actor.DisplayNameSnapshot is null)
        {
            return actor;
        }

        var displayName = AuditValueRedactor.Redact("displayName", AuditValue.Known(
            actor.DisplayNameSnapshot, AuditValueSensitivity.NonSensitive));
        return displayName.Kind == AuditValueKind.Redacted
            ? actor with { DisplayNameSnapshot = "[REDACTED]" }
            : actor;
    }

    private static AuditFieldValue RedactValue(string settingName, AuditFieldValue value)
    {
        if (!Enum.IsDefined(value.State) || !Enum.IsDefined(value.Sensitivity))
        {
            throw new ArgumentException("Audit values require defined state and sensitivity values.");
        }

        if (value.State is AuditValueState.Unknown or AuditValueState.Absent)
        {
            if (value.Value is not null || value.Sensitivity != AuditValueSensitivity.ClassifiedUnknown)
            {
                throw new ArgumentException("Unknown and absent audit values must remain distinct and contain no value.");
            }

            return value;
        }

        if (value.State == AuditValueState.Redacted)
        {
            if (value.Value is not { ValueKind: JsonValueKind.String } redacted || redacted.GetString() != "[REDACTED]")
            {
                throw new ArgumentException("Redacted audit values must use the explicit marker.");
            }

            return AuditFieldValue.Redacted();
        }

        if (value.Value is null || value.Sensitivity == AuditValueSensitivity.ClassifiedUnknown)
        {
            throw new ArgumentException("Known audit values require an explicit sensitivity classification.");
        }

        var domainValue = AuditValue.Known(value.Value.Value.GetRawText(), value.Sensitivity);
        if (AuditValueRedactor.Redact(settingName, domainValue).Kind != AuditValueKind.Known)
        {
            return AuditFieldValue.Redacted();
        }

        if (value.Sensitivity == AuditValueSensitivity.Sensitive)
        {
            return AuditFieldValue.Redacted();
        }

        var sanitized = SanitizeNode(JsonNode.Parse(value.Value.Value.GetRawText()), settingName);
        return sanitized is null
            ? AuditFieldValue.Known(JsonSerializer.SerializeToElement<object?>(null), AuditValueSensitivity.NonSensitive)
            : AuditFieldValue.Known(JsonSerializer.SerializeToElement(sanitized), AuditValueSensitivity.NonSensitive);
    }

    private static JsonNode? SanitizeNode(JsonNode? node, string propertyName)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var key in jsonObject.Select(item => item.Key).ToArray())
            {
                if (AuditValueRedactor.Redact(key, AuditValue.Known("value", AuditValueSensitivity.NonSensitive)).Kind != AuditValueKind.Known)
                {
                    jsonObject[key] = "[REDACTED]";
                }
                else
                {
                    jsonObject[key] = SanitizeNode(jsonObject[key], key);
                }
            }

            return jsonObject;
        }

        if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                jsonArray[index] = SanitizeNode(jsonArray[index], propertyName);
            }

            return jsonArray;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) &&
            AuditValueRedactor.Redact(propertyName, AuditValue.Known(text, AuditValueSensitivity.NonSensitive)).Kind != AuditValueKind.Known)
        {
            return JsonValue.Create("[REDACTED]");
        }

        return node;
    }

    private static void ValidateEvent(SettingAuditEvent value)
    {
        if (value.EventId == Guid.Empty || value.OccurredAtUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(value.EventKind) || !Enum.IsDefined(value.Outcome) ||
            string.IsNullOrWhiteSpace(value.ServiceName) || value.ServiceName.Length > 256 ||
            string.IsNullOrWhiteSpace(value.ResourceId) || value.ResourceId.Length > 2048 ||
            string.IsNullOrWhiteSpace(value.SettingName) || value.SettingName.Length > 512 ||
            value.RequestId == Guid.Empty || value.OperationId == Guid.Empty || value.CorrelationId == Guid.Empty ||
            (value.PlanId == Guid.Empty) || (value.ApprovalId == Guid.Empty))
        {
            throw new ArgumentException("Audit events require UTC time, event and correlation IDs, setting scope, and outcome.");
        }

        ValidateActor(value.Requester);
        ValidateActor(value.Executor);
        if (value.Approver is not null)
        {
            ValidateActor(value.Approver);
        }
    }

    private static void ValidateActor(TrustedAuditActor actor)
    {
        if (!Enum.IsDefined(actor.Kind) || actor.SubjectId is { Length: > 256 } ||
            actor.DisplayNameSnapshot is { Length: > 256 } ||
            (actor.Kind is AuditActorKind.User or AuditActorKind.Workload && string.IsNullOrWhiteSpace(actor.SubjectId)) ||
            (actor.Kind is AuditActorKind.Scheduled or AuditActorKind.External or AuditActorKind.Unknown && actor.SubjectId is not null) ||
            (actor.Kind != AuditActorKind.User && actor.DisplayNameSnapshot is not null))
        {
            throw new ArgumentException("Audit actors must preserve trusted user, workload, scheduled, external, or unknown identity.");
        }
    }

    private static void ValidateFilter(AuditQueryFilter filter)
    {
        if (filter.Limit is < 1 or > 500 ||
            filter.FromUtc?.Offset is { } fromOffset && fromOffset != TimeSpan.Zero ||
            filter.ToUtc?.Offset is { } toOffset && toOffset != TimeSpan.Zero ||
            filter.BeforeUtc?.Offset is { } beforeOffset && beforeOffset != TimeSpan.Zero ||
            filter.FromUtc > filter.ToUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), "Audit filters require UTC ranges and limits between 1 and 500.");
        }
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}