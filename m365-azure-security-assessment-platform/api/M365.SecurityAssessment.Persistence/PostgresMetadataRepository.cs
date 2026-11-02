using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace M365.SecurityAssessment.Persistence;

public sealed class PostgresMetadataRepository(NpgsqlDataSource dataSource) : IMetadataRepository
{
    public async Task AppendAsync(VersionedMetadataRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);
        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, record.Scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO platform.metadata_records
                (organization_id, customer_tenant_id, record_id, record_kind, version, previous_version,
                 payload, parent_kind, parent_record_id, parent_version)
            VALUES
                (@organization_id, @customer_tenant_id, @record_id, @record_kind, @version, @previous_version,
                 @payload, @parent_kind, @parent_record_id, @parent_version)
            """, scoped.Connection, scoped.Transaction);
        command.Parameters.AddWithValue("organization_id", record.Scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", record.Scope.CustomerTenantId);
        command.Parameters.AddWithValue("record_id", record.RecordId);
        command.Parameters.AddWithValue("record_kind", ToDatabaseKind(record.Kind));
        command.Parameters.AddWithValue("version", record.Version);
        AddParameter(command, "previous_version", NpgsqlDbType.Integer, record.Version == 1 ? null : record.Version - 1);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = record.Payload.GetRawText() });
        AddParameter(command, "parent_kind", NpgsqlDbType.Text,
            record.Parent is null ? null : ToDatabaseKind(record.Parent.Kind));
        AddParameter(command, "parent_record_id", NpgsqlDbType.Uuid, record.Parent?.RecordId);
        AddParameter(command, "parent_version", NpgsqlDbType.Integer, record.Parent?.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await scoped.CommitAsync(cancellationToken);
    }

    public async Task<VersionedMetadataRecord?> GetAsync(
        CustomerDataScope scope,
        MetadataRecordKind kind,
        Guid recordId,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty || version <= 0 || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("A metadata kind, record ID, and positive version are required.");
        }

        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT record_id, record_kind, version, payload::text, parent_kind, parent_record_id, parent_version
            FROM platform.metadata_records
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND record_kind = @record_kind AND record_id = @record_id AND version = @version
            """, scoped.Connection, scoped.Transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("record_kind", ToDatabaseKind(kind));
        command.Parameters.AddWithValue("record_id", recordId);
        command.Parameters.AddWithValue("version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await scoped.CommitAsync(cancellationToken);
            return null;
        }

        var result = ReadRecord(scope, reader);
        await reader.DisposeAsync();
        await scoped.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<VersionedMetadataRecord>> ListAsync(
        CustomerDataScope scope,
        MetadataRecordKind kind,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Metadata list limits must be between 1 and 500.");
        }

        await using var scoped = await ScopedTransaction.BeginAsync(dataSource, scope, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT record_id, record_kind, version, payload::text, parent_kind, parent_record_id, parent_version
            FROM platform.metadata_records
            WHERE organization_id = @organization_id AND customer_tenant_id = @customer_tenant_id
              AND record_kind = @record_kind
            ORDER BY created_at_utc DESC, record_id, version DESC
            LIMIT @limit
            """, scoped.Connection, scoped.Transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("record_kind", ToDatabaseKind(kind));
        command.Parameters.AddWithValue("limit", limit);
        var records = new List<VersionedMetadataRecord>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(ReadRecord(scope, reader));
            }
        }

        await scoped.CommitAsync(cancellationToken);
        return records;
    }

    internal static void AddScope(NpgsqlCommand command, CustomerDataScope scope)
    {
        command.Parameters.AddWithValue("organization_id", scope.OrganizationId);
        command.Parameters.AddWithValue("customer_tenant_id", scope.CustomerTenantId);
    }

    internal static void AddParameter(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    internal static string ToDatabaseKind(MetadataRecordKind kind) => kind switch
    {
        MetadataRecordKind.Profile => "profile",
        MetadataRecordKind.Assessment => "assessment",
        MetadataRecordKind.Finding => "finding",
        MetadataRecordKind.Exception => "exception",
        MetadataRecordKind.EmergencyRegistry => "emergency_registry",
        MetadataRecordKind.Plan => "plan",
        MetadataRecordKind.Approval => "approval",
        MetadataRecordKind.OperationState => "operation_state",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static MetadataRecordKind FromDatabaseKind(string kind) => kind switch
    {
        "profile" => MetadataRecordKind.Profile,
        "assessment" => MetadataRecordKind.Assessment,
        "finding" => MetadataRecordKind.Finding,
        "exception" => MetadataRecordKind.Exception,
        "emergency_registry" => MetadataRecordKind.EmergencyRegistry,
        "plan" => MetadataRecordKind.Plan,
        "approval" => MetadataRecordKind.Approval,
        "operation_state" => MetadataRecordKind.OperationState,
        _ => throw new InvalidOperationException("Database returned an unknown metadata kind.")
    };

    private static void ValidateRecord(VersionedMetadataRecord record)
    {
        if (record.RecordId == Guid.Empty || record.Version <= 0 || !Enum.IsDefined(record.Kind) ||
            record.Payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Metadata records require a valid kind, ID, version, and JSON payload.");
        }

        if (record.Parent is { } parent &&
            (parent.RecordId == Guid.Empty || parent.Version <= 0 || !Enum.IsDefined(parent.Kind)))
        {
            throw new ArgumentException("Parent references require a valid kind, ID, and version.");
        }
    }

    private static VersionedMetadataRecord ReadRecord(CustomerDataScope scope, NpgsqlDataReader reader)
    {
        var payload = JsonDocument.Parse(reader.GetString(3)).RootElement.Clone();
        MetadataRecordReference? parent = reader.IsDBNull(4)
            ? null
            : new MetadataRecordReference(FromDatabaseKind(reader.GetString(4)), reader.GetGuid(5), reader.GetInt32(6));
        return new VersionedMetadataRecord(scope, reader.GetGuid(0), FromDatabaseKind(reader.GetString(1)),
            reader.GetInt32(2), payload, parent);
    }
}