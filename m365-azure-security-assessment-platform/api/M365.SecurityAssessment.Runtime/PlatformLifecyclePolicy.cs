namespace M365.SecurityAssessment.Runtime;

public sealed record DevelopmentOperationalDefaults(
    int MaximumActiveJobsPerCustomer,
    TimeSpan JobTimeout,
    int MaximumTransientRetries,
    TimeSpan MaximumRetryBackoff,
    TimeSpan QueueAgeAlertThreshold,
    TimeSpan UncertainWriteReconciliationWindow,
    TimeSpan SyntheticFixtureRetention,
    int TestBudgetAlertPercent,
    int TestBudgetCriticalPercent,
    TimeSpan RecoveryTestRtoTarget,
    TimeSpan RecoveryTestRpoTarget,
    bool ProductionEligible)
{
    public static DevelopmentOperationalDefaults Proposed { get; } = new(
        2,
        TimeSpan.FromMinutes(30),
        5,
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromDays(14),
        75,
        90,
        TimeSpan.FromHours(4),
        TimeSpan.FromMinutes(15),
        false);
}

public sealed record ProductionApproval<T>(
    T Value,
    string ApprovalId,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

public sealed record ApprovedProductionInputs(
    ProductionApproval<string>? Geography,
    ProductionApproval<string>? EvidenceRetentionPolicy,
    ProductionApproval<string>? AuditRetentionPolicy,
    ProductionApproval<string>? BackupPolicy,
    ProductionApproval<string>? SloProfile,
    ProductionApproval<string>? ScaleProfile,
    ProductionApproval<string>? NetworkProfile,
    ProductionApproval<string>? Budget,
    ProductionApproval<string>? ServiceBusPremiumCapacity);

public enum ActivationDecision
{
    AllowedSyntheticDevelopment,
    AllowedApprovedProduction,
    BlockedSyntheticDataRequired,
    BlockedProductionApprovalsMissing,
    BlockedProductionTier,
    BlockedUnapprovedOperationalDefaults
}

public static class ProductionActivationPolicy
{
    public static ActivationDecision Evaluate(
        RuntimeEnvironment environment,
        bool syntheticDataOnly,
        ApprovedProductionInputs? productionInputs,
        bool usingServiceBusPremium,
        bool usingDevelopmentDefaults,
        DateTimeOffset nowUtc)
    {
        if (environment is RuntimeEnvironment.Development or RuntimeEnvironment.Test)
        {
            return syntheticDataOnly
                ? ActivationDecision.AllowedSyntheticDevelopment
                : ActivationDecision.BlockedSyntheticDataRequired;
        }

        if (environment != RuntimeEnvironment.Production)
        {
            return ActivationDecision.BlockedProductionApprovalsMissing;
        }

        if (usingDevelopmentDefaults)
        {
            return ActivationDecision.BlockedUnapprovedOperationalDefaults;
        }

        if (!usingServiceBusPremium)
        {
            return ActivationDecision.BlockedProductionTier;
        }

        if (productionInputs is null || new[]
            {
                productionInputs.Geography,
                productionInputs.EvidenceRetentionPolicy,
                productionInputs.AuditRetentionPolicy,
                productionInputs.BackupPolicy,
                productionInputs.SloProfile,
                productionInputs.ScaleProfile,
                productionInputs.NetworkProfile,
                productionInputs.Budget,
                productionInputs.ServiceBusPremiumCapacity
            }.Any(approval => !IsApproved(approval, nowUtc)))
        {
            return ActivationDecision.BlockedProductionApprovalsMissing;
        }

        return ActivationDecision.AllowedApprovedProduction;
    }

    private static bool IsApproved(ProductionApproval<string>? approval, DateTimeOffset nowUtc) =>
        approval is not null &&
        !string.IsNullOrWhiteSpace(approval.Value) &&
        !string.IsNullOrWhiteSpace(approval.ApprovalId) &&
        !string.IsNullOrWhiteSpace(approval.ApprovedBy) &&
        approval.ApprovedAtUtc.Offset == TimeSpan.Zero &&
        approval.ApprovedAtUtc <= nowUtc;
}

public enum LifecycleStepStatus
{
    Pending,
    Completed,
    Failed,
    Unknown
}

public sealed record OffboardingFacts(
    LifecycleStepStatus JobAdmissionStopStatus,
    LifecycleStepStatus ConnectionDisableStatus,
    LifecycleStepStatus QueuedWriteCancellationStatus,
    LifecycleStepStatus InFlightOperationReconciliationStatus,
    bool LegalHoldActive,
    string RetentionPolicy,
    DateTimeOffset? BackupExpiryUtc,
    IReadOnlyList<string> RemainingExternalGrantDescriptions,
    IReadOnlyList<string> RetainedRecordCategories);

public sealed record OffboardingReport(
    LifecycleStepStatus JobAdmissionStopStatus,
    LifecycleStepStatus ConnectionDisableStatus,
    LifecycleStepStatus QueuedWriteCancellationStatus,
    LifecycleStepStatus InFlightOperationReconciliationStatus,
    bool EvidenceDeletionDeferredByLegalHold,
    string RetentionPolicy,
    DateTimeOffset? BackupExpiryUtc,
    IReadOnlyList<string> RemainingExternalGrants,
    IReadOnlyList<string> RetainedRecords);

public static class CustomerOffboardingPolicy
{
    public static OffboardingReport CreateReport(OffboardingFacts facts) => new(
        JobAdmissionStopStatus: facts.JobAdmissionStopStatus,
        ConnectionDisableStatus: facts.ConnectionDisableStatus,
        QueuedWriteCancellationStatus: facts.QueuedWriteCancellationStatus,
        InFlightOperationReconciliationStatus: facts.InFlightOperationReconciliationStatus,
        EvidenceDeletionDeferredByLegalHold: facts.LegalHoldActive,
        RetentionPolicy: facts.RetentionPolicy,
        BackupExpiryUtc: facts.BackupExpiryUtc,
        RemainingExternalGrants: facts.RemainingExternalGrantDescriptions.ToArray(),
        RetainedRecords: facts.RetainedRecordCategories.ToArray());
}

public interface ICustomerLifecycleStore
{
    ValueTask ActivateCustomerStopAsync(CustomerPartition partition, CancellationToken cancellationToken);
    ValueTask DisableCustomerConnectionsAsync(CustomerPartition partition, CancellationToken cancellationToken);
    ValueTask CancelQueuedWritesAsync(CustomerPartition partition, CancellationToken cancellationToken);
    ValueTask<OffboardingFacts> ReadOffboardingFactsAsync(CustomerPartition partition, CancellationToken cancellationToken);
}

public sealed record RestoredAuthorizationState(
    bool ExecutionAdmissionDisabled,
    bool CredentialsQuarantined,
    bool ApprovalsQuarantined,
    bool JobsQuarantined,
    bool ConnectionsQuarantined,
    bool CurrentAuthorizationReconciled,
    bool CurrentJobStateReconciled,
    bool CurrentApprovalStateReconciled);

public static class RestoreQuarantinePolicy
{
    public static RestoredAuthorizationState EnterQuarantine() => new(
        ExecutionAdmissionDisabled: true,
        CredentialsQuarantined: true,
        ApprovalsQuarantined: true,
        JobsQuarantined: true,
        ConnectionsQuarantined: true,
        CurrentAuthorizationReconciled: false,
        CurrentJobStateReconciled: false,
        CurrentApprovalStateReconciled: false);

    public static bool CanEnableExecution(RestoredAuthorizationState state) =>
        !state.ExecutionAdmissionDisabled &&
        !state.CredentialsQuarantined &&
        !state.ApprovalsQuarantined &&
        !state.JobsQuarantined &&
        !state.ConnectionsQuarantined &&
        state.CurrentAuthorizationReconciled &&
        state.CurrentJobStateReconciled &&
        state.CurrentApprovalStateReconciled;

    public static RestoredAuthorizationState Reconcile(
        RestoredAuthorizationState quarantinedState,
        bool authorizationIsCurrent,
        bool jobStateIsCurrent,
        bool approvalStateIsCurrent) => quarantinedState with
        {
            ExecutionAdmissionDisabled = !(authorizationIsCurrent && jobStateIsCurrent && approvalStateIsCurrent),
            CredentialsQuarantined = !authorizationIsCurrent,
            ApprovalsQuarantined = !approvalStateIsCurrent,
            JobsQuarantined = !jobStateIsCurrent,
            ConnectionsQuarantined = !authorizationIsCurrent,
            CurrentAuthorizationReconciled = authorizationIsCurrent,
            CurrentJobStateReconciled = jobStateIsCurrent,
            CurrentApprovalStateReconciled = approvalStateIsCurrent
        };
}