using System.Collections.Immutable;

namespace M365.SecurityAssessment.Runtime;

public enum OperationState
{
    Draft,
    AwaitingApproval,
    Approved,
    Queued,
    Preflight,
    Executing,
    PendingVerification,
    Verified,
    Blocked,
    Expired,
    Failed,
    Cancelled,
    Unknown,
    PartiallyCompleted
}

public enum OperationEvent
{
    SubmitForApproval,
    Approve,
    Queue,
    BeginPreflight,
    BeginExecution,
    WriteAccepted,
    WriteRejected,
    ResponseLost,
    RemoteReadCompleted,
    VerificationPassed,
    VerificationPending,
    VerificationFailed,
    StopRequested,
    Expire,
    LaterOperationFailed
}

public sealed record OperationTransitionContext(
    bool RemoteReadCompleted,
    bool StopActive,
    bool AnyEarlierOperationApplied,
    bool PlanExpired);

public static class OperationStateMachine
{
    public static OperationState Transition(
        OperationState current,
        OperationEvent @event,
        OperationTransitionContext context)
    {
        if (context.PlanExpired && current is OperationState.Draft or OperationState.AwaitingApproval or
            OperationState.Approved or OperationState.Queued or OperationState.Preflight)
        {
            return OperationState.Expired;
        }

        return (current, @event) switch
        {
            (OperationState.Draft, OperationEvent.SubmitForApproval) => OperationState.AwaitingApproval,
            (OperationState.Draft, OperationEvent.StopRequested) => OperationState.Cancelled,
            (OperationState.AwaitingApproval, OperationEvent.Approve) => OperationState.Approved,
            (OperationState.AwaitingApproval, OperationEvent.StopRequested) => OperationState.Cancelled,
            (OperationState.Approved, OperationEvent.Queue) => OperationState.Queued,
            (OperationState.Approved, OperationEvent.StopRequested) => OperationState.Cancelled,
            (OperationState.Queued, OperationEvent.BeginPreflight) when !context.StopActive => OperationState.Preflight,
            (OperationState.Queued, OperationEvent.BeginPreflight) => OperationState.Blocked,
            (OperationState.Queued, OperationEvent.StopRequested) => OperationState.Cancelled,
            (OperationState.Preflight, OperationEvent.BeginExecution) when !context.StopActive => OperationState.Executing,
            (OperationState.Preflight, OperationEvent.BeginExecution) => OperationState.Blocked,
            (OperationState.Preflight, OperationEvent.StopRequested) => OperationState.Cancelled,
            (OperationState.Executing, OperationEvent.WriteAccepted) => OperationState.PendingVerification,
            (OperationState.Executing, OperationEvent.ResponseLost) => OperationState.Unknown,
            (OperationState.Executing, OperationEvent.WriteRejected) => context.AnyEarlierOperationApplied
                ? OperationState.PartiallyCompleted
                : OperationState.Failed,
            (OperationState.Executing, OperationEvent.StopRequested) => OperationState.Unknown,
            (OperationState.PendingVerification, OperationEvent.VerificationPassed) => OperationState.Verified,
            (OperationState.PendingVerification, OperationEvent.VerificationPending) => OperationState.PendingVerification,
            (OperationState.PendingVerification, OperationEvent.VerificationFailed) => OperationState.Failed,
            (OperationState.PendingVerification, OperationEvent.StopRequested) => OperationState.PendingVerification,
            (OperationState.Unknown, OperationEvent.RemoteReadCompleted) when context.RemoteReadCompleted => OperationState.PendingVerification,
            (OperationState.Unknown, OperationEvent.VerificationPassed) when context.RemoteReadCompleted => OperationState.Verified,
            (OperationState.Unknown, OperationEvent.VerificationFailed) when context.RemoteReadCompleted => OperationState.Failed,
            (OperationState.Unknown, OperationEvent.LaterOperationFailed) => OperationState.PartiallyCompleted,
            (OperationState.PartiallyCompleted, OperationEvent.RemoteReadCompleted) when context.RemoteReadCompleted => OperationState.PendingVerification,
            (OperationState.PartiallyCompleted, OperationEvent.StopRequested) => OperationState.PartiallyCompleted,
            _ => current
        };
    }

    public static OperationState Aggregate(IEnumerable<OperationState> operations)
    {
        var states = operations.ToArray();
        if (states.Length == 0)
        {
            return OperationState.Blocked;
        }

        if (states.All(state => state == OperationState.Verified))
        {
            return OperationState.Verified;
        }

        if (states.Any(state => state == OperationState.Unknown))
        {
            return OperationState.Unknown;
        }

        if (states.Any(state => state is OperationState.Failed or OperationState.Blocked or OperationState.Cancelled) &&
            states.Any(state => state is OperationState.Verified or OperationState.PendingVerification))
        {
            return OperationState.PartiallyCompleted;
        }

        if (states.Any(state => state == OperationState.PendingVerification))
        {
            return OperationState.PendingVerification;
        }

        return states.First(state => state != OperationState.Verified);
    }
}

public sealed record OperationPreflightEvidence(
    bool SettingReadKnown,
    string? ObservedValueJson,
    string? ETag,
    bool TargetScopeComplete,
    bool PermissionKnownAndSufficient,
    bool ConnectionCurrent,
    string? CurrentEmergencyRegistryRevision,
    EmergencyPreflightDecision EmergencyAccessDecision,
    bool AuditStoreAvailable,
    bool StopFlagKnown,
    bool StopActive,
    bool AuthorizationCurrent,
    DateTimeOffset CollectedAtUtc);

public enum PreflightBlocker
{
    StaleOrUnknownRead,
    UnknownOrIncompleteScope,
    MissingPermission,
    RevokedOrStaleConnection,
    EmergencyRegistryChanged,
    EmergencyAccessNotReady,
    AuditUnavailable,
    StopFlagUnknown,
    StopActive,
    AuthorizationChanged,
    EvidenceExpired,
    TargetChanged,
    ETagMissingOrChanged
}

public sealed record PreflightDecision(bool Allowed, ImmutableArray<PreflightBlocker> Blockers)
{
    public static PreflightDecision Allow { get; } = new(true, ImmutableArray<PreflightBlocker>.Empty);
}

public static class OperationPreflightPolicy
{
    public static PreflightDecision Evaluate(
        ChangePlanRevision plan,
        PlannedSettingChange change,
        OperationPreflightEvidence evidence,
        DateTimeOffset nowUtc,
        TimeSpan maximumEvidenceAge)
    {
        var blockers = ImmutableArray.CreateBuilder<PreflightBlocker>();
        if (!evidence.SettingReadKnown || evidence.ObservedValueJson is null)
        {
            blockers.Add(PreflightBlocker.StaleOrUnknownRead);
        }
        else if (!string.Equals(evidence.ObservedValueJson, change.BeforeValueJson, StringComparison.Ordinal))
        {
            blockers.Add(PreflightBlocker.TargetChanged);
        }

        if (!evidence.TargetScopeComplete)
        {
            blockers.Add(PreflightBlocker.UnknownOrIncompleteScope);
        }

        if (!evidence.PermissionKnownAndSufficient)
        {
            blockers.Add(PreflightBlocker.MissingPermission);
        }

        if (!evidence.ConnectionCurrent)
        {
            blockers.Add(PreflightBlocker.RevokedOrStaleConnection);
        }

        if (!string.Equals(evidence.CurrentEmergencyRegistryRevision, plan.EmergencyRegistryRevision, StringComparison.Ordinal))
        {
            blockers.Add(PreflightBlocker.EmergencyRegistryChanged);
        }

        if (change.LockoutSensitive && evidence.EmergencyAccessDecision != EmergencyPreflightDecision.Ready)
        {
            blockers.Add(PreflightBlocker.EmergencyAccessNotReady);
        }

        if (!evidence.AuditStoreAvailable)
        {
            blockers.Add(PreflightBlocker.AuditUnavailable);
        }

        if (!evidence.StopFlagKnown)
        {
            blockers.Add(PreflightBlocker.StopFlagUnknown);
        }
        else if (evidence.StopActive)
        {
            blockers.Add(PreflightBlocker.StopActive);
        }

        if (!evidence.AuthorizationCurrent)
        {
            blockers.Add(PreflightBlocker.AuthorizationChanged);
        }

        if (nowUtc.Offset != TimeSpan.Zero || evidence.CollectedAtUtc.Offset != TimeSpan.Zero ||
            evidence.CollectedAtUtc > nowUtc || nowUtc - evidence.CollectedAtUtc > maximumEvidenceAge)
        {
            blockers.Add(PreflightBlocker.EvidenceExpired);
        }

        if (string.IsNullOrWhiteSpace(evidence.ETag) ||
            !string.Equals(evidence.ETag, change.ExpectedETag, StringComparison.Ordinal))
        {
            blockers.Add(PreflightBlocker.ETagMissingOrChanged);
        }

        var result = blockers.ToImmutable();
        return result.IsDefaultOrEmpty ? PreflightDecision.Allow : new PreflightDecision(false, result);
    }
}

public sealed record OperationIntentKey(
    string OrganizationId,
    string CustomerTenantId,
    string PlanRevisionId,
    string PlanContentHash,
    string OperationId);

public sealed record ResourceLeaseKey(string CustomerTenantId, string Service, string ResourceId, string SettingPath);

public interface IOperationIdempotencyStore
{
    ValueTask<OperationState?> FindAsync(OperationIntentKey key, CancellationToken cancellationToken);
    ValueTask<bool> TryStartAsync(OperationIntentKey key, CancellationToken cancellationToken);
}

public enum DuplicateOperationDecision
{
    StartNew,
    ReturnStoredOutcome,
    ReconcileRemoteState,
    ContinueExistingOperation
}

public static class OperationIdempotencyPolicy
{
    public static DuplicateOperationDecision Evaluate(OperationState? existingState) => existingState switch
    {
        null => DuplicateOperationDecision.StartNew,
        OperationState.Unknown or OperationState.PendingVerification => DuplicateOperationDecision.ReconcileRemoteState,
        OperationState.Queued or OperationState.Preflight or OperationState.Executing => DuplicateOperationDecision.ContinueExistingOperation,
        _ => DuplicateOperationDecision.ReturnStoredOutcome
    };
}

public interface IResourceLeaseStore
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        ResourceLeaseKey key,
        string operationId,
        TimeSpan duration,
        CancellationToken cancellationToken);
}

public interface ISettingStateReader
{
    ValueTask<OperationPreflightEvidence> ReadPreflightAsync(
        ChangePlanRevision plan,
        PlannedSettingChange change,
        CancellationToken cancellationToken);

    ValueTask<(bool Known, string? ValueJson, string? ETag)> ReadCurrentAsync(
        CustomerPartition partition,
        string service,
        string resourceId,
        string settingPath,
        CancellationToken cancellationToken);
}

public interface IConditionalSettingWriter
{
    ValueTask<bool> TryWriteAsync(
        CustomerPartition partition,
        PlannedSettingChange change,
        string expectedETag,
        CancellationToken cancellationToken);
}

public interface IDurableAuditLedger
{
    ValueTask<bool> TryAppendIntentAsync(SettingAuditIntent intent, CancellationToken cancellationToken);
    ValueTask AppendResultAsync(SettingAuditResult result, CancellationToken cancellationToken);
}

public interface ICustomerStopFlagReader
{
    ValueTask<(bool Known, bool Stopped)> ReadAsync(CustomerPartition partition, CancellationToken cancellationToken);
}

public sealed record OperationFeasibilityEvidence(
    string OperationId,
    string SourceRevision,
    bool ReadEvidenceVerified,
    bool ScopeVerified,
    bool PermissionsVerified,
    bool ConditionalWriteVerified,
    bool PostWriteVerificationVerified,
    TimeSpan? MaximumPropagationWindow,
    bool RecoveryDocumented,
    bool FixtureTestsPassed);

public static class OperationQualificationPolicy
{
    public static bool IsFeasibilityComplete(OperationFeasibilityEvidence evidence) =>
        !string.IsNullOrWhiteSpace(evidence.OperationId) &&
        !string.IsNullOrWhiteSpace(evidence.SourceRevision) &&
        evidence.ReadEvidenceVerified && evidence.ScopeVerified && evidence.PermissionsVerified &&
        evidence.ConditionalWriteVerified && evidence.PostWriteVerificationVerified &&
        evidence.MaximumPropagationWindow is { } window && window >= TimeSpan.Zero &&
        evidence.RecoveryDocumented && evidence.FixtureTestsPassed;
}

public static class MutationAllowlist
{
    public static ImmutableHashSet<string> EnabledOperationIds { get; } = ImmutableHashSet<string>.Empty;

    public static bool IsEnabled(string operationId) => EnabledOperationIds.Contains(operationId);
}

public sealed record RuntimeComponentVersions(
    ImmutableHashSet<int> SupportedJobContractVersions,
    ImmutableHashSet<int> SupportedOperationContractVersions);

public static class RuntimeCompatibilityPolicy
{
    public static bool SupportsJob(RuntimeComponentVersions versions, int contractVersion) =>
        versions.SupportedJobContractVersions.Contains(contractVersion);

    public static bool SupportsOperation(RuntimeComponentVersions versions, int contractVersion) =>
        versions.SupportedOperationContractVersions.Contains(contractVersion);
}

public interface IOperationQualificationStore
{
    ValueTask<OperationFeasibilityEvidence?> FindCurrentVerifiedAsync(
        string operationId,
        CancellationToken cancellationToken);
}