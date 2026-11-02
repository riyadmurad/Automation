using System.Text.Json;
using M365.SecurityAssessment.Domain;

namespace M365.SecurityAssessment.Persistence;

public enum MetadataRecordKind
{
    Profile,
    Assessment,
    Finding,
    Exception,
    EmergencyRegistry,
    Plan,
    Approval,
    OperationState
}

public sealed record MetadataRecordReference(MetadataRecordKind Kind, Guid RecordId, int Version);

public sealed record VersionedMetadataRecord(
    CustomerDataScope Scope,
    Guid RecordId,
    MetadataRecordKind Kind,
    int Version,
    JsonElement Payload,
    MetadataRecordReference? Parent = null);

public interface IMetadataRepository
{
    Task AppendAsync(VersionedMetadataRecord record, CancellationToken cancellationToken = default);
    Task<VersionedMetadataRecord?> GetAsync(
        CustomerDataScope scope,
        MetadataRecordKind kind,
        Guid recordId,
        int version,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VersionedMetadataRecord>> ListAsync(
        CustomerDataScope scope,
        MetadataRecordKind kind,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public sealed record EncryptedSessionEnvelope(
    CustomerDataScope Scope,
    Guid UserObjectId,
    Guid SessionId,
    ReadOnlyMemory<byte> Ciphertext,
    ReadOnlyMemory<byte> Nonce,
    ReadOnlyMemory<byte> AuthenticationTag,
    ReadOnlyMemory<byte> WrappedDataKey,
    string KeyId,
    int KeyVersion,
    DateTimeOffset ExpiresAtUtc);

public interface ISessionEnvelopeProtector
{
    Task<EncryptedSessionEnvelope> ProtectAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        ReadOnlyMemory<byte> plaintext,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default);
    Task<ReadOnlyMemory<byte>> UnprotectAsync(
        EncryptedSessionEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public interface IEncryptedSessionRepository
{
    Task StoreAsync(EncryptedSessionEnvelope envelope, CancellationToken cancellationToken = default);
    Task<EncryptedSessionEnvelope?> GetActiveAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
    Task RevokeAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public enum AuditActorKind { User, Workload, Scheduled, External, Unknown }
public enum AuditEventKind { Intent, Result, Verification, Reconciliation }
public enum AuditOutcome { Requested, Applied, Verified, Failed, Blocked, Cancelled, Partial, PendingVerification, Unknown, ExternalDrift }
public enum AuditValueState { Known, Unknown, Absent, Redacted }

public sealed record TrustedAuditActor(AuditActorKind Kind, string? SubjectId, string? DisplayNameSnapshot = null)
{
    public static TrustedAuditActor User(string subjectId, string? displayNameSnapshot = null) =>
        new(AuditActorKind.User, subjectId, displayNameSnapshot);

    public static TrustedAuditActor Workload(string subjectId) => new(AuditActorKind.Workload, subjectId);
    public static TrustedAuditActor Scheduled() => new(AuditActorKind.Scheduled, null);
    public static TrustedAuditActor External() => new(AuditActorKind.External, null);
    public static TrustedAuditActor Unknown() => new(AuditActorKind.Unknown, null);
}

public sealed record AuditFieldValue(AuditValueState State, JsonElement? Value, AuditValueSensitivity Sensitivity)
{
    public static AuditFieldValue Known(JsonElement value, AuditValueSensitivity sensitivity) =>
        new(AuditValueState.Known, value.Clone(), sensitivity);

    public static AuditFieldValue Unknown() =>
        new(AuditValueState.Unknown, null, AuditValueSensitivity.ClassifiedUnknown);

    public static AuditFieldValue Absent() =>
        new(AuditValueState.Absent, null, AuditValueSensitivity.ClassifiedUnknown);

    public static AuditFieldValue Redacted() =>
        new(AuditValueState.Redacted, JsonSerializer.SerializeToElement("[REDACTED]"), AuditValueSensitivity.Sensitive);
}

public sealed record SettingAuditEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    CustomerDataScope Scope,
    TrustedAuditActor Requester,
    TrustedAuditActor? Approver,
    TrustedAuditActor Executor,
    AuditEventKind EventKind,
    string ServiceName,
    string ResourceId,
    string SettingName,
    AuditFieldValue PreviousValue,
    AuditFieldValue RequestedValue,
    AuditFieldValue VerifiedValue,
    AuditOutcome Outcome,
    Guid RequestId,
    Guid? PlanId,
    Guid? ApprovalId,
    Guid OperationId,
    Guid CorrelationId);

public sealed record AuditQueryFilter(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? AuthorizedUserId = null,
    string? ServiceName = null,
    string? SettingName = null,
    string? ResourceId = null,
    AuditOutcome? Outcome = null,
    int Limit = 100,
    DateTimeOffset? BeforeUtc = null);

public sealed record AuditIntegrityResult(bool IsValid, int EventCount, Guid? FirstInvalidEventId);

public interface IAuditLedgerRepository
{
    Task AppendAsync(SettingAuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SettingAuditEvent>> QueryAsync(
        CustomerDataScope scope,
        AuditQueryFilter filter,
        CancellationToken cancellationToken = default);
    Task<AuditIntegrityResult> VerifyIntegrityAsync(
        CustomerDataScope scope,
        CancellationToken cancellationToken = default);
}

public sealed record EvidenceBlobReference(
    CustomerDataScope Scope,
    Guid EvidenceId,
    string Sha256,
    long ByteLength,
    string ContentType);

public interface IEvidenceBlobStore
{
    Task<EvidenceBlobReference> PutAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        Stream content,
        string contentType,
        string expectedSha256,
        CancellationToken cancellationToken = default);
    Task<Stream> OpenVerifiedReadAsync(
        EvidenceBlobReference reference,
        CancellationToken cancellationToken = default);
}

public interface IEvidenceRepository
{
    Task<EvidenceBlobReference> IngestAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        Stream content,
        string contentType,
        string expectedSha256,
        CancellationToken cancellationToken = default);
    Task<Stream> OpenVerifiedReadAsync(
        CustomerDataScope scope,
        Guid evidenceId,
        CancellationToken cancellationToken = default);
}