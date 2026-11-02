using System.Collections.Immutable;

namespace M365.SecurityAssessment.Runtime;

public sealed record TransactionalOutboxRecord(
    string OutboxId,
    ServiceBusJobEnvelope Envelope,
    DateTimeOffset CreatedAtUtc,
    int DispatchAttempts,
    bool Dispatched);

public interface ITransactionalOutboxStore
{
    ValueTask PersistWithJobAsync(TransactionalOutboxRecord record, CancellationToken cancellationToken);
    ValueTask<ImmutableArray<TransactionalOutboxRecord>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken);
    ValueTask MarkDispatchedAfterBrokerAcknowledgementAsync(string outboxId, CancellationToken cancellationToken);
}

public interface IServiceBusTransport
{
    ValueTask<bool> PublishAsync(ServiceBusJobEnvelope envelope, CancellationToken cancellationToken);
}

public enum OutboxDispatchStatus
{
    Dispatched,
    PendingTransportUnavailable,
    BrokerDidNotAcknowledge,
    StoreUnavailable
}

public sealed record OutboxDispatchResult(string? OutboxId, OutboxDispatchStatus Status);

public sealed class OutboxDispatcher(
    ITransactionalOutboxStore store,
    IServiceBusTransport? transport)
{
    public const int MaximumBatchSize = 100;

    public async ValueTask<ImmutableArray<OutboxDispatchResult>> DispatchBatchAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0 || maximumCount > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        ImmutableArray<TransactionalOutboxRecord> pending;
        try
        {
            pending = await store.ReadPendingAsync(maximumCount, cancellationToken);
        }
        catch
        {
            return [new OutboxDispatchResult(null, OutboxDispatchStatus.StoreUnavailable)];
        }

        if (transport is null)
        {
            return pending.Select(record => new OutboxDispatchResult(
                record.OutboxId,
                OutboxDispatchStatus.PendingTransportUnavailable)).ToImmutableArray();
        }

        var results = ImmutableArray.CreateBuilder<OutboxDispatchResult>(pending.Length);
        foreach (var record in pending)
        {
            bool acknowledged;
            try
            {
                acknowledged = await transport.PublishAsync(record.Envelope, cancellationToken);
            }
            catch
            {
                results.Add(new OutboxDispatchResult(record.OutboxId, OutboxDispatchStatus.BrokerDidNotAcknowledge));
                continue;
            }

            if (!acknowledged)
            {
                results.Add(new OutboxDispatchResult(record.OutboxId, OutboxDispatchStatus.BrokerDidNotAcknowledge));
                continue;
            }

            try
            {
                await store.MarkDispatchedAfterBrokerAcknowledgementAsync(record.OutboxId, cancellationToken);
                results.Add(new OutboxDispatchResult(record.OutboxId, OutboxDispatchStatus.Dispatched));
            }
            catch
            {
                results.Add(new OutboxDispatchResult(record.OutboxId, OutboxDispatchStatus.StoreUnavailable));
            }
        }

        return results.ToImmutable();
    }
}

public sealed record RemediationAdmissionRequest(
    ChangePlanRevision Plan,
    PlannedSettingChange Change,
    PlanApproval? Approval,
    ApprovalPolicy ApprovalPolicy,
    string CurrentAuthorizationVersion,
    OperationPreflightEvidence FreshEvidence,
    DateTimeOffset NowUtc,
    TimeSpan MaximumEvidenceAge,
    OperationFeasibilityEvidence? QualificationEvidence = null);

public enum RemediationAdmissionFailure
{
    None,
    PlanHashMismatch,
    PurposeMismatch,
    ApprovalRejected,
    PreflightRejected,
    OperationNotQualified,
    OperationNotAllowlisted
}

public sealed record RemediationAdmissionResult(
    bool Admitted,
    RemediationAdmissionFailure Failure,
    ApprovalDecision ApprovalDecision,
    PreflightDecision PreflightDecision);

public static class RemediationAdmissionPolicy
{
    public static RemediationAdmissionResult Evaluate(RemediationAdmissionRequest request)
    {
        if (!string.Equals(
                request.Plan.ContentHash,
                ChangePlanFactory.RecomputeContentHash(request.Plan),
                StringComparison.Ordinal))
        {
            return Denied(RemediationAdmissionFailure.PlanHashMismatch, ApprovalDecision.WrongPlan, []);
        }

        if (request.Plan.Purpose != ExecutionPurpose.Remediation)
        {
            return Denied(RemediationAdmissionFailure.PurposeMismatch, ApprovalDecision.WrongPlan, []);
        }

        if (!request.Plan.Changes.Contains(request.Change))
        {
            return Denied(RemediationAdmissionFailure.PlanHashMismatch, ApprovalDecision.WrongPlan, []);
        }

        var approvalDecision = PlanApprovalPolicy.Evaluate(
            request.Plan,
            request.Approval,
            request.ApprovalPolicy,
            request.CurrentAuthorizationVersion,
            request.NowUtc);
        if (approvalDecision != ApprovalDecision.Approved)
        {
            return Denied(RemediationAdmissionFailure.ApprovalRejected, approvalDecision, []);
        }

        var preflight = OperationPreflightPolicy.Evaluate(
            request.Plan,
            request.Change,
            request.FreshEvidence,
            request.NowUtc,
            request.MaximumEvidenceAge);
        if (!preflight.Allowed)
        {
            return Denied(RemediationAdmissionFailure.PreflightRejected, approvalDecision, preflight.Blockers);
        }

        if (request.QualificationEvidence is null ||
            !string.Equals(request.QualificationEvidence.OperationId, request.Change.OperationId, StringComparison.Ordinal) ||
            !OperationQualificationPolicy.IsFeasibilityComplete(request.QualificationEvidence))
        {
            return Denied(RemediationAdmissionFailure.OperationNotQualified, approvalDecision, []);
        }

        if (!MutationAllowlist.IsEnabled(request.Change.OperationId))
        {
            return Denied(RemediationAdmissionFailure.OperationNotAllowlisted, approvalDecision, []);
        }

        return new RemediationAdmissionResult(true, RemediationAdmissionFailure.None, approvalDecision, preflight);
    }

    private static RemediationAdmissionResult Denied(
        RemediationAdmissionFailure failure,
        ApprovalDecision approval,
        IEnumerable<PreflightBlocker> blockers) => new(
            false,
            failure,
            approval,
            blockers.Any()
                ? new PreflightDecision(false, blockers.ToImmutableArray())
                : PreflightDecision.Allow);
}

public enum RecoveryPlanDecision
{
    Created,
    InvalidOriginalPlan,
    TenantMismatch,
    RegistryChanged,
    RemoteStateUnknown,
    InterveningChangeDetected,
    MissingETag
}

public sealed record RecoveryPlanResult(RecoveryPlanDecision Decision, ChangePlanRevision? Plan);

public static class RecoveryPlanFactory
{
    public static RecoveryPlanResult TryCreate(
        ChangePlanRevision originalPlan,
        PlannedSettingChange originalChange,
        CustomerPartition partition,
        EmergencyRegistryRevision currentRegistry,
        bool currentValueKnown,
        string? currentValueJson,
        string? currentETag,
        string recoveryRevisionId,
        string requesterId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        if (!string.Equals(
                originalPlan.ContentHash,
                ChangePlanFactory.RecomputeContentHash(originalPlan),
                StringComparison.Ordinal))
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.InvalidOriginalPlan, null);
        }

        if (!string.Equals(originalPlan.OrganizationId, partition.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(originalPlan.CustomerTenantId, partition.CustomerTenantId, StringComparison.Ordinal))
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.TenantMismatch, null);
        }

        if (!EmergencyAccessPolicy.PlanUsesCurrentRegistry(originalPlan, currentRegistry))
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.RegistryChanged, null);
        }

        if (!currentValueKnown || currentValueJson is null)
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.RemoteStateUnknown, null);
        }

        if (!string.Equals(currentValueJson, originalChange.RequestedValueJson, StringComparison.Ordinal))
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.InterveningChangeDetected, null);
        }

        if (string.IsNullOrWhiteSpace(currentETag))
        {
            return new RecoveryPlanResult(RecoveryPlanDecision.MissingETag, null);
        }

        var recoveryChange = originalChange with
        {
            OperationId = $"recovery-{originalChange.OperationId}-{recoveryRevisionId}",
            BeforeValueJson = currentValueJson,
            RequestedValueJson = originalChange.BeforeValueJson,
            ExpectedETag = currentETag
        };
        var plan = ChangePlanFactory.Create(
            originalPlan.OrganizationId,
            originalPlan.CustomerTenantId,
            ExecutionPurpose.Remediation,
            recoveryRevisionId,
            requesterId,
            originalPlan.BaselineVersion,
            originalPlan.ProfileVersion,
            originalPlan.AdapterVersion,
            originalPlan.EvidenceFingerprint,
            currentRegistry.RevisionId,
            "Recovery of a previously approved operation; requires fresh approval.",
            originalPlan.RecoveryLimitations,
            expiresAtUtc,
            [recoveryChange],
            nowUtc);
        return new RecoveryPlanResult(RecoveryPlanDecision.Created, plan);
    }
}

public sealed record RuntimeRoutingDecision(bool Allowed, string Reason);

public static class AdapterRuntimePolicy
{
    public static RuntimeRoutingDecision Evaluate(AdapterRuntime required, AdapterRuntime available) =>
        required == available
            ? new RuntimeRoutingDecision(true, string.Empty)
            : new RuntimeRoutingDecision(false, "The required adapter runtime is unavailable; the job remains blocked.");
}

public enum UncertainWriteDisposition
{
    RemainUnknown,
    VerifyObservedRequestedValue,
    RetryMayBeConsideredAfterFreshPreflight,
    ConflictRequiresNewPlan
}

public static class UncertainWriteReconciliationPolicy
{
    public static UncertainWriteDisposition Evaluate(
        PlannedSettingChange change,
        bool remoteReadKnown,
        string? observedValueJson)
    {
        if (!remoteReadKnown || observedValueJson is null)
        {
            return UncertainWriteDisposition.RemainUnknown;
        }

        if (string.Equals(observedValueJson, change.RequestedValueJson, StringComparison.Ordinal))
        {
            return UncertainWriteDisposition.VerifyObservedRequestedValue;
        }

        if (string.Equals(observedValueJson, change.BeforeValueJson, StringComparison.Ordinal))
        {
            return UncertainWriteDisposition.RetryMayBeConsideredAfterFreshPreflight;
        }

        return UncertainWriteDisposition.ConflictRequiresNewPlan;
    }
}