using System.Collections.Immutable;
using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class OutboxAndLifecycleTests
{
    [Fact]
    public async Task MissingServiceBusTransportLeavesOutboxPendingAndNeverMarksItDurableAsSent()
    {
        var store = new TestOutboxStore([OutboxRecord()]);
        var dispatcher = new OutboxDispatcher(store, transport: null);

        var result = await dispatcher.DispatchBatchAsync(10, CancellationToken.None);

        Assert.Equal(OutboxDispatchStatus.PendingTransportUnavailable, Assert.Single(result).Status);
        Assert.Empty(store.MarkedDispatched);
        Assert.False(store.Records[0].Dispatched);
    }

    [Fact]
    public async Task BrokerFailureOrLostAcknowledgementLeavesOutboxPending()
    {
        var store = new TestOutboxStore([OutboxRecord()]);
        var dispatcher = new OutboxDispatcher(store, new TestServiceBusTransport(acknowledge: false));

        var result = await dispatcher.DispatchBatchAsync(10, CancellationToken.None);

        Assert.Equal(OutboxDispatchStatus.BrokerDidNotAcknowledge, Assert.Single(result).Status);
        Assert.Empty(store.MarkedDispatched);
    }

    [Fact]
    public async Task OutboxIsMarkedOnlyAfterBrokerAcknowledgement()
    {
        var record = OutboxRecord();
        var store = new TestOutboxStore([record]);
        var transport = new TestServiceBusTransport(acknowledge: true);
        var dispatcher = new OutboxDispatcher(store, transport);

        var result = await dispatcher.DispatchBatchAsync(10, CancellationToken.None);

        Assert.Equal(OutboxDispatchStatus.Dispatched, Assert.Single(result).Status);
        Assert.Contains(record.OutboxId, store.MarkedDispatched);
        Assert.Equal(1, transport.PublishCount);
    }

    [Fact]
    public async Task OutboxBatchIsBoundedAndStoreFailureIsVisible()
    {
        var store = new TestOutboxStore([]) { ThrowOnRead = true };
        var dispatcher = new OutboxDispatcher(store, transport: null);

        var failed = await dispatcher.DispatchBatchAsync(10, CancellationToken.None);

        Assert.Equal(OutboxDispatchStatus.StoreUnavailable, Assert.Single(failed).Status);
        Assert.Null(failed[0].OutboxId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await dispatcher.DispatchBatchAsync(OutboxDispatcher.MaximumBatchSize + 1, CancellationToken.None));
    }

    [Fact]
    public void DevelopmentDefaultsMatchDesignAndCannotBeProductionPolicy()
    {
        var defaults = DevelopmentOperationalDefaults.Proposed;

        Assert.Equal(2, defaults.MaximumActiveJobsPerCustomer);
        Assert.Equal(TimeSpan.FromMinutes(30), defaults.JobTimeout);
        Assert.Equal(5, defaults.MaximumTransientRetries);
        Assert.Equal(TimeSpan.FromMinutes(15), defaults.MaximumRetryBackoff);
        Assert.Equal(TimeSpan.FromMinutes(10), defaults.QueueAgeAlertThreshold);
        Assert.Equal(TimeSpan.FromMinutes(10), defaults.UncertainWriteReconciliationWindow);
        Assert.Equal(TimeSpan.FromDays(14), defaults.SyntheticFixtureRetention);
        Assert.Equal(75, defaults.TestBudgetAlertPercent);
        Assert.Equal(90, defaults.TestBudgetCriticalPercent);
        Assert.Equal(TimeSpan.FromHours(4), defaults.RecoveryTestRtoTarget);
        Assert.Equal(TimeSpan.FromMinutes(15), defaults.RecoveryTestRpoTarget);
        Assert.False(defaults.ProductionEligible);
    }

    [Fact]
    public void DevelopmentActivationRequiresSyntheticData()
    {
        Assert.Equal(ActivationDecision.AllowedSyntheticDevelopment,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Development, true, null, false, true, RuntimeTestData.Now));
        Assert.Equal(ActivationDecision.BlockedSyntheticDataRequired,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Test, false, null, false, true, RuntimeTestData.Now));
    }

    [Fact]
    public void ProductionActivationRequiresAllApprovedInputsPremiumAndNonDevelopmentDefaults()
    {
        var complete = ApprovedInputs();

        Assert.Equal(ActivationDecision.BlockedProductionApprovalsMissing,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Production, false, null, true, false, RuntimeTestData.Now));
        Assert.Equal(ActivationDecision.BlockedProductionTier,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Production, false, complete, false, false, RuntimeTestData.Now));
        Assert.Equal(ActivationDecision.BlockedUnapprovedOperationalDefaults,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Production, false, complete, true, true, RuntimeTestData.Now));
        Assert.Equal(ActivationDecision.AllowedApprovedProduction,
            ProductionActivationPolicy.Evaluate(RuntimeEnvironment.Production, false, complete, true, false, RuntimeTestData.Now));
    }

    [Fact]
    public void OffboardingReportPreservesIncompleteStepsLegalHoldExternalGrantsAndBackupExpiry()
    {
        var facts = new OffboardingFacts(
            LifecycleStepStatus.Completed,
            LifecycleStepStatus.Completed,
            LifecycleStepStatus.Unknown,
            LifecycleStepStatus.Pending,
            true,
            "approved-365-day-retention",
            RuntimeTestData.Now.AddDays(40),
            ["Customer must remove remaining delegated grant"],
            ["Audit events", "Evidence snapshots"]);

        var report = CustomerOffboardingPolicy.CreateReport(facts);

        Assert.Equal(LifecycleStepStatus.Completed, report.JobAdmissionStopStatus);
        Assert.Equal(LifecycleStepStatus.Unknown, report.QueuedWriteCancellationStatus);
        Assert.Equal(LifecycleStepStatus.Pending, report.InFlightOperationReconciliationStatus);
        Assert.True(report.EvidenceDeletionDeferredByLegalHold);
        Assert.Contains("Customer must remove remaining delegated grant", report.RemainingExternalGrants);
        Assert.Equal(facts.BackupExpiryUtc, report.BackupExpiryUtc);
    }

    [Fact]
    public void RestoreStaysQuarantinedUntilAuthorizationJobsAndApprovalsAreReconciled()
    {
        var quarantined = RestoreQuarantinePolicy.EnterQuarantine();
        var partial = RestoreQuarantinePolicy.Reconcile(quarantined, true, true, false);
        var ready = RestoreQuarantinePolicy.Reconcile(quarantined, true, true, true);

        Assert.False(RestoreQuarantinePolicy.CanEnableExecution(quarantined));
        Assert.False(RestoreQuarantinePolicy.CanEnableExecution(partial));
        Assert.True(RestoreQuarantinePolicy.CanEnableExecution(ready));
    }

    [Fact]
    public void RecoveryRequiresExactObservedStateAndProducesNewPlan()
    {
        var change = RuntimeTestData.Change();
        var original = RuntimeTestData.Plan([change]);
        var registry = RuntimeTestData.EmergencyRegistry();
        var created = RecoveryPlanFactory.TryCreate(
            original, change, RuntimeTestData.Customer, registry, true, change.RequestedValueJson,
            "etag-current", "recovery-revision", "operator-b", RuntimeTestData.Now.AddHours(1), RuntimeTestData.Now);
        var conflict = RecoveryPlanFactory.TryCreate(
            original, change, RuntimeTestData.Customer, registry, true, "\"intervening-edit\"",
            "etag-current", "recovery-revision-2", "operator-b", RuntimeTestData.Now.AddHours(1), RuntimeTestData.Now);

        Assert.Equal(RecoveryPlanDecision.Created, created.Decision);
        Assert.Equal(change.RequestedValueJson, Assert.Single(created.Plan!.Changes).BeforeValueJson);
        Assert.Equal(change.BeforeValueJson, Assert.Single(created.Plan.Changes).RequestedValueJson);
        Assert.NotEqual(original.ContentHash, created.Plan.ContentHash);
        Assert.Equal(RecoveryPlanDecision.InterveningChangeDetected, conflict.Decision);
        Assert.Null(conflict.Plan);

        var tampered = original with { ContentHash = "tampered" };
        var rejected = RecoveryPlanFactory.TryCreate(
            tampered, change, RuntimeTestData.Customer, registry, true, change.RequestedValueJson,
            "etag-current", "recovery-revision-3", "operator-b", RuntimeTestData.Now.AddHours(1), RuntimeTestData.Now);
        Assert.Equal(RecoveryPlanDecision.InvalidOriginalPlan, rejected.Decision);
    }

    [Fact]
    public void IncompatibleJobContractIsRejected()
    {
        var versions = new RuntimeComponentVersions(
            ImmutableHashSet.Create(1, 2), ImmutableHashSet.Create(1));

        Assert.True(RuntimeCompatibilityPolicy.SupportsJob(versions, 2));
        Assert.False(RuntimeCompatibilityPolicy.SupportsJob(versions, 3));
        Assert.False(RuntimeCompatibilityPolicy.SupportsOperation(versions, 2));
    }

    private static ApprovedProductionInputs ApprovedInputs() => new(
        Approved("region-approved"),
        Approved("evidence-retention-approved"),
        Approved("audit-retention-approved"),
        Approved("backup-policy-approved"),
        Approved("production-slo-approved"),
        Approved("scale-profile-approved"),
        Approved("network-profile-approved"),
        Approved("budget-approved"),
        Approved("service-bus-premium-capacity-approved"));

    private static ProductionApproval<string> Approved(string value) => new(
        value, $"approval-{value}", "independent-approver", RuntimeTestData.Now.AddDays(-1));

    private static TransactionalOutboxRecord OutboxRecord() => new(
        "outbox-a",
        new ServiceBusJobEnvelope(
            1, RuntimeTestData.Customer, "job-a", "connection-a", "connection-rev-a",
            ExecutionPurpose.Assessment, "credential-ref-a", "adapter-a", AdapterRuntime.Linux,
            RuntimeTestData.Now.AddHours(1)),
        RuntimeTestData.Now,
        0,
        false);

    private sealed class TestOutboxStore(ImmutableArray<TransactionalOutboxRecord> records) : ITransactionalOutboxStore
    {
        public ImmutableArray<TransactionalOutboxRecord> Records { get; private set; } = records;
        public List<string> MarkedDispatched { get; } = [];
        public bool ThrowOnRead { get; init; }

        public ValueTask PersistWithJobAsync(TransactionalOutboxRecord record, CancellationToken cancellationToken)
        {
            Records = Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ImmutableArray<TransactionalOutboxRecord>> ReadPendingAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Test store failure.");
            }

            return ValueTask.FromResult(
                Records.Where(record => !record.Dispatched).Take(maximumCount).ToImmutableArray());
        }

        public ValueTask MarkDispatchedAfterBrokerAcknowledgementAsync(string outboxId, CancellationToken cancellationToken)
        {
            MarkedDispatched.Add(outboxId);
            Records = Records.Select(record => record.OutboxId == outboxId ? record with { Dispatched = true } : record)
                .ToImmutableArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestServiceBusTransport(bool acknowledge) : IServiceBusTransport
    {
        public int PublishCount { get; private set; }

        public ValueTask<bool> PublishAsync(ServiceBusJobEnvelope envelope, CancellationToken cancellationToken)
        {
            PublishCount++;
            return ValueTask.FromResult(acknowledge);
        }
    }
}