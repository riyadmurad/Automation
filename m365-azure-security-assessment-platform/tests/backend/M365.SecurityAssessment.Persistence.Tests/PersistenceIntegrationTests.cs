using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using M365.SecurityAssessment.Domain;
using M365.SecurityAssessment.Persistence;
using Npgsql;

namespace M365.SecurityAssessment.Persistence.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MetadataIsTenantScopedAcrossPooledConnectionsAndRejectsCrossTenantReferences()
    {
        var scopeA = NewScope();
        var scopeB = NewCustomerScope(scopeA);
        await fixture.RegisterScopeAsync(scopeA);
        await fixture.RegisterScopeAsync(scopeB);
        var repository = new PostgresMetadataRepository(fixture.ApplicationDataSource);
        var recordIdA = Guid.NewGuid();
        var recordIdB = Guid.NewGuid();
        using var firstPayload = JsonDocument.Parse("""{"nullable":null,"unknown":{"state":"unknown"},"redacted":{"state":"redacted","value":"[REDACTED]"}}""");
        using var secondPayload = JsonDocument.Parse("""{"customer":"B"}""");

        await repository.AppendAsync(new VersionedMetadataRecord(
            scopeA, recordIdA, MetadataRecordKind.Profile, 1, firstPayload.RootElement));
        await repository.AppendAsync(new VersionedMetadataRecord(
            scopeB, recordIdB, MetadataRecordKind.Profile, 1, secondPayload.RootElement));

        var aRecord = await repository.GetAsync(scopeA, MetadataRecordKind.Profile, recordIdA, 1);
        var aCannotReadB = await repository.GetAsync(scopeA, MetadataRecordKind.Profile, recordIdB, 1);
        var bRecord = await repository.GetAsync(scopeB, MetadataRecordKind.Profile, recordIdB, 1);
        Assert.NotNull(aRecord);
        Assert.Null(aCannotReadB);
        Assert.NotNull(bRecord);
        Assert.Equal(JsonValueKind.Null, aRecord.Payload.GetProperty("nullable").ValueKind);
        Assert.False(aRecord.Payload.TryGetProperty("absent", out _));
        Assert.Equal("unknown", aRecord.Payload.GetProperty("unknown").GetProperty("state").GetString());
        Assert.Equal("redacted", aRecord.Payload.GetProperty("redacted").GetProperty("state").GetString());

        await using (var connection = await fixture.ApplicationDataSource.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("SELECT count(*) FROM platform.metadata_records", connection))
        {
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var connection = await fixture.ApplicationDataSource.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("""
            INSERT INTO platform.customer_scope (organization_id, customer_tenant_id)
            VALUES (@organization_id, @customer_tenant_id)
            """, connection))
        {
            command.Parameters.AddWithValue("organization_id", Guid.NewGuid());
            command.Parameters.AddWithValue("customer_tenant_id", Guid.NewGuid());
            await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        }

        var crossCustomerParent = new VersionedMetadataRecord(scopeA, Guid.NewGuid(), MetadataRecordKind.Finding,
            1, secondPayload.RootElement,
            new MetadataRecordReference(MetadataRecordKind.Profile, recordIdB, 1));
        await Assert.ThrowsAsync<PostgresException>(() => repository.AppendAsync(crossCustomerParent));

        Assert.Null(await repository.GetAsync(scopeA, MetadataRecordKind.Profile, recordIdB, 1));
        Assert.NotNull(await repository.GetAsync(scopeB, MetadataRecordKind.Profile, recordIdB, 1));
    }

    [Fact]
    public async Task MetadataVersionsAreAppendOnlyAndRequireAnExistingPriorVersion()
    {
        var scope = NewScope();
        await fixture.RegisterScopeAsync(scope);
        var repository = new PostgresMetadataRepository(fixture.ApplicationDataSource);
        var recordId = Guid.NewGuid();
        using var v1 = JsonDocument.Parse("""{"revision":1}""");
        using var v2 = JsonDocument.Parse("""{"revision":2}""");
        await repository.AppendAsync(new VersionedMetadataRecord(scope, recordId, MetadataRecordKind.Plan, 1, v1.RootElement));
        await repository.AppendAsync(new VersionedMetadataRecord(scope, recordId, MetadataRecordKind.Plan, 2, v2.RootElement));

        Assert.Equal(1, (await repository.GetAsync(scope, MetadataRecordKind.Plan, recordId, 1))!.Payload.GetProperty("revision").GetInt32());
        Assert.Equal(2, (await repository.GetAsync(scope, MetadataRecordKind.Plan, recordId, 2))!.Payload.GetProperty("revision").GetInt32());
        await repository.AppendAsync(new VersionedMetadataRecord(scope, recordId, MetadataRecordKind.Plan, 3, v2.RootElement));
        await Assert.ThrowsAsync<PostgresException>(() => repository.AppendAsync(
            new VersionedMetadataRecord(scope, recordId, MetadataRecordKind.Plan, 5, v2.RootElement)));

        var approvalId = Guid.NewGuid();
        await repository.AppendAsync(new VersionedMetadataRecord(scope, approvalId, MetadataRecordKind.Approval, 1, v1.RootElement));
        await Assert.ThrowsAsync<PostgresException>(() => repository.AppendAsync(
            new VersionedMetadataRecord(scope, approvalId, MetadataRecordKind.Approval, 2, v2.RootElement)));

        await using var connection = await fixture.ApplicationDataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand(
            "UPDATE platform.metadata_records SET payload = '{}'::jsonb WHERE record_id = @record_id", connection);
        update.Parameters.AddWithValue("record_id", recordId);
        await Assert.ThrowsAsync<PostgresException>(async () => await update.ExecuteNonQueryAsync());
        await using var delete = new NpgsqlCommand(
            "DELETE FROM platform.metadata_records WHERE record_id = @record_id", connection);
        delete.Parameters.AddWithValue("record_id", recordId);
        await Assert.ThrowsAsync<PostgresException>(async () => await delete.ExecuteNonQueryAsync());

    }

    [Fact]
    public async Task SessionsArePartitionedByTenantUserAndSessionAndRevocationCannotBeReversed()
    {
        var scopeA = NewScope();
        var scopeB = NewCustomerScope(scopeA);
        await fixture.RegisterScopeAsync(scopeA);
        await fixture.RegisterScopeAsync(scopeB);
        var repository = new PostgresEncryptedSessionRepository(fixture.ApplicationDataSource);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var envelope = NewSession(scopeA, userId, sessionId);

        await repository.StoreAsync(envelope);
        Assert.NotNull(await repository.GetActiveAsync(scopeA, userId, sessionId));
        Assert.Null(await repository.GetActiveAsync(scopeB, userId, sessionId));
        await repository.RevokeAsync(scopeA, userId, sessionId);
        Assert.Null(await repository.GetActiveAsync(scopeA, userId, sessionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StoreAsync(envelope));
    }

    [Fact]
    public async Task AuditAppendsTrustedSettingEventsRedactsBeforeStorageAndProvidesSafeFilteredExports()
    {
        var scope = NewScope();
        var otherCustomer = NewCustomerScope(scope);
        await fixture.RegisterScopeAsync(scope);
        await fixture.RegisterScopeAsync(otherCustomer);
        var repository = new PostgresAuditLedgerRepository(fixture.ApplicationDataSource);
        var operationId = Guid.NewGuid();
        var incomplete = NewAuditEvent(scope, operationId, " ", AuditEventKind.Intent, AuditOutcome.Requested, "resource");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.AppendAsync(incomplete));
        var first = NewAuditEvent(scope, operationId, "authentication.policy", AuditEventKind.Intent,
            AuditOutcome.Requested, "=SUM(1,1)");
        var second = NewAuditEvent(scope, operationId, "authentication.policy", AuditEventKind.Result,
            AuditOutcome.Unknown, "=SUM(1,1)") with
        {
            PreviousValue = KnownJson("""{"mode":"strict","apiToken":"never-persist-this"}"""),
            VerifiedValue = AuditFieldValue.Absent()
        };
        var third = NewAuditEvent(scope, operationId, "password", AuditEventKind.Verification,
            AuditOutcome.Verified, "resource-3") with
        {
            PreviousValue = KnownJson("\"old-secret-value\"", AuditValueSensitivity.Sensitive),
            RequestedValue = AuditFieldValue.Unknown(),
            VerifiedValue = AuditFieldValue.Redacted()
        };
        await repository.AppendAsync(first);
        await repository.AppendAsync(second);
        await repository.AppendAsync(third);
        var reconciliation = NewAuditEvent(scope, operationId, "authentication.policy", AuditEventKind.Reconciliation,
            AuditOutcome.Verified, "=SUM(1,1)") with
        {
            PreviousValue = KnownJson("\"requested\""),
            RequestedValue = AuditFieldValue.Unknown(),
            VerifiedValue = KnownJson("\"effective\"")
        };
        await repository.AppendAsync(reconciliation);

        var audit = await repository.QueryAsync(scope, new AuditQueryFilter(
            AuthorizedUserId: "validated-user-17", ServiceName: "Microsoft Entra ID", Limit: 10));
        Assert.Empty(await repository.QueryAsync(otherCustomer, new AuditQueryFilter(Limit: 10)));
        Assert.Equal(4, audit.Count);
        Assert.All(audit, item =>
        {
            Assert.Equal(TimeSpan.Zero, item.OccurredAtUtc.Offset);
            Assert.Equal(scope, item.Scope);
            Assert.Equal("validated-user-17", item.Requester.SubjectId);
            Assert.Equal("approved-user-22", item.Approver?.SubjectId);
            Assert.Equal("assessment-worker-3", item.Executor.SubjectId);
            Assert.Equal(operationId, item.OperationId);
            Assert.NotEqual(Guid.Empty, item.CorrelationId);
        });
        Assert.Contains(audit, item => item.Outcome == AuditOutcome.Unknown && item.VerifiedValue.State == AuditValueState.Absent);
        Assert.Contains(audit, item => item.EventKind == AuditEventKind.Reconciliation &&
            item.VerifiedValue.Value?.GetString() == "effective");
        Assert.Contains(audit, item => item.SettingName == "password" &&
            item.PreviousValue.State == AuditValueState.Redacted && item.VerifiedValue.State == AuditValueState.Redacted);
        Assert.DoesNotContain("never-persist-this", AuditExportFormatter.ToJson(audit), StringComparison.Ordinal);
        Assert.Contains("\"'=SUM(1,1)\"", AuditExportFormatter.ToCsv(audit), StringComparison.Ordinal);
        var untrustedExport = first with
        {
            Requester = TrustedAuditActor.User("validated-user-17", "Bearer deliberately-secret-snapshot"),
            RequestedValue = KnownJson("\"Bearer deliberately-secret-value\"")
        };
        Assert.DoesNotContain("deliberately-secret", AuditExportFormatter.ToJson([untrustedExport]), StringComparison.Ordinal);
        Assert.DoesNotContain("deliberately-secret", AuditExportFormatter.ToCsv([untrustedExport]), StringComparison.Ordinal);
        var integrity = await repository.VerifyIntegrityAsync(scope);
        Assert.True(integrity.IsValid);
        Assert.Equal(4, integrity.EventCount);

        await using var connection = await fixture.ApplicationDataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand(
            "UPDATE platform.audit_events SET event_hash = repeat('0', 64) WHERE event_id = @event_id", connection);
        update.Parameters.AddWithValue("event_id", first.EventId);
        await Assert.ThrowsAsync<PostgresException>(async () => await update.ExecuteNonQueryAsync());
        await using var delete = new NpgsqlCommand(
            "DELETE FROM platform.audit_events WHERE event_id = @event_id", connection);
        delete.Parameters.AddWithValue("event_id", first.EventId);
        await Assert.ThrowsAsync<PostgresException>(async () => await delete.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task AuditHashChainDetectsPrivilegedTamperingWithoutChangingEventHistory()
    {
        var scope = NewScope();
        await fixture.RegisterScopeAsync(scope);
        var repository = new PostgresAuditLedgerRepository(fixture.ApplicationDataSource);
        var auditEvent = NewAuditEvent(scope, Guid.NewGuid(), "sharing.defaultLink", AuditEventKind.Result,
            AuditOutcome.Applied, "resource-1");
        await repository.AppendAsync(auditEvent);
        Assert.True((await repository.VerifyIntegrityAsync(scope)).IsValid);

        await using var connection = await fixture.AdminDataSource.OpenConnectionAsync();
        await using (var disableTriggers = new NpgsqlCommand("SET session_replication_role = replica", connection))
        {
            await disableTriggers.ExecuteNonQueryAsync();
        }

        try
        {
            await using var tamper = new NpgsqlCommand("""
                UPDATE platform.audit_events
                SET canonical_payload = replace(canonical_payload, '"outcome":"applied"', '"outcome":"failed"')
                WHERE event_id = @event_id
                """, connection);
            tamper.Parameters.AddWithValue("event_id", auditEvent.EventId);
            Assert.Equal(1, await tamper.ExecuteNonQueryAsync());
        }
        finally
        {
            await using var restoreTriggers = new NpgsqlCommand("RESET session_replication_role", connection);
            await restoreTriggers.ExecuteNonQueryAsync();
        }

        var result = await repository.VerifyIntegrityAsync(scope);
        Assert.False(result.IsValid);
        Assert.Equal(auditEvent.EventId, result.FirstInvalidEventId);
    }

    [Fact]
    public async Task EvidenceRequiresScopedMetadataAndRevalidatesBlobHashBeforeReturningContent()
    {
        var scopeA = NewScope();
        var scopeB = NewCustomerScope(scopeA);
        await fixture.RegisterScopeAsync(scopeA);
        await fixture.RegisterScopeAsync(scopeB);
        var root = Path.Combine(Path.GetTempPath(), "m365-persistence-tests", Guid.NewGuid().ToString("N"));
        var blobStore = new LocalEvidenceBlobStore(root);
        var repository = new PostgresEvidenceRepository(fixture.ApplicationDataSource, blobStore);
        var evidenceId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("synthetic-evidence");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using var source = new MemoryStream(bytes, writable: false);

        var reference = await repository.IngestAsync(scopeA, evidenceId, source, "application/json", digest);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.OpenVerifiedReadAsync(scopeB, evidenceId));
        await using (var verified = await repository.OpenVerifiedReadAsync(scopeA, evidenceId))
        using (var copy = new MemoryStream())
        {
            await verified.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        var storedFile = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(storedFile, "tampered synthetic evidence");
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.OpenVerifiedReadAsync(scopeA, evidenceId));
        Assert.Equal(digest, reference.Sha256);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task DatabaseOutageCannotReturnSuccessfulAuditOrEvidenceAppend()
    {
        var scope = NewScope();
        await using var unavailable = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=disabled;Timeout=1;Command Timeout=1;Pooling=false");
        var audit = new PostgresAuditLedgerRepository(unavailable);
        await Assert.ThrowsAnyAsync<Exception>(() => audit.AppendAsync(
            NewAuditEvent(scope, Guid.NewGuid(), "setting", AuditEventKind.Intent, AuditOutcome.Requested, "resource")));

        var root = Path.Combine(Path.GetTempPath(), "m365-persistence-outage", Guid.NewGuid().ToString("N"));
        var store = new PostgresEvidenceRepository(unavailable, new LocalEvidenceBlobStore(root));
        var body = Encoding.UTF8.GetBytes("synthetic-outage-evidence");
        var hash = Convert.ToHexStringLower(SHA256.HashData(body));
        await using var content = new MemoryStream(body, writable: false);
        await Assert.ThrowsAnyAsync<Exception>(() => store.IngestAsync(
            scope, Guid.NewGuid(), content, "application/json", hash));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void PersistenceRegistrationRequiresAnExplicitConnectionString()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var exception = Record.Exception(() => { services.AddPlatformPersistence(configuration); });
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Throws<InvalidOperationException>(() => services.AddLocalEvidenceStore(configuration, "unused"));

        configuration["ASPNETCORE_ENVIRONMENT"] = "Development";
        configuration["PlatformPersistence:EvidenceMode"] = "synthetic-local";
        services.AddLocalEvidenceStore(configuration, "unused");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEvidenceBlobStore));
    }

    private static CustomerDataScope NewScope() => new(Guid.NewGuid(), Guid.NewGuid());
    private static CustomerDataScope NewCustomerScope(CustomerDataScope organization) =>
        new(organization.OrganizationId, Guid.NewGuid());

    private static EncryptedSessionEnvelope NewSession(CustomerDataScope scope, Guid userId, Guid sessionId) =>
        new(scope, userId, sessionId, new byte[] { 0x11, 0x22, 0x33 }, new byte[] { 0x01, 0x02, 0x03 },
            new byte[] { 0x04, 0x05 }, new byte[] { 0x06, 0x07, 0x08 },
            "test-key-version", 1, DateTimeOffset.UtcNow.AddMinutes(10));

    private static AuditFieldValue KnownJson(
        string json,
        AuditValueSensitivity sensitivity = AuditValueSensitivity.NonSensitive)
    {
        using var document = JsonDocument.Parse(json);
        return AuditFieldValue.Known(document.RootElement, sensitivity);
    }

    private static SettingAuditEvent NewAuditEvent(
        CustomerDataScope scope,
        Guid operationId,
        string settingName,
        AuditEventKind kind,
        AuditOutcome outcome,
        string resourceId) => new(
            Guid.NewGuid(), DateTimeOffset.UtcNow, scope,
            TrustedAuditActor.User("validated-user-17", "Operator snapshot"),
            TrustedAuditActor.User("approved-user-22", "Approver snapshot"),
            TrustedAuditActor.Workload("assessment-worker-3"), kind, "Microsoft Entra ID", resourceId, settingName,
            KnownJson("\"before\""), KnownJson("\"requested\""), AuditFieldValue.Unknown(), outcome,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), operationId, Guid.NewGuid());
}