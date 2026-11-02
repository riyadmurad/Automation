using System.Collections.Immutable;
using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class ExecutionBindingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly CustomerPartition Customer = new("org-a", "tenant-a");

    [Fact]
    public void ValidStoredOwnershipAndWorkerBindingIsAdmitted()
    {
        var (envelope, stored, worker) = ValidBinding();

        var result = ExecutionBindingPolicy.Evaluate(envelope, stored, worker, Now);

        Assert.True(result.IsValid);
        Assert.Equal(ExecutionBindingFailure.None, result.Failure);
    }

    [Theory]
    [InlineData("tenant", ExecutionBindingFailure.TenantMismatch)]
    [InlineData("purpose", ExecutionBindingFailure.PurposeMismatch)]
    [InlineData("credential", ExecutionBindingFailure.CredentialReferenceMismatch)]
    [InlineData("revision", ExecutionBindingFailure.StaleConnectionRevision)]
    [InlineData("connection", ExecutionBindingFailure.ConnectionDisabled)]
    [InlineData("adapter", ExecutionBindingFailure.AdapterNotAllowed)]
    [InlineData("runtime", ExecutionBindingFailure.WorkerRuntimeMismatch)]
    [InlineData("worker-tenant", ExecutionBindingFailure.WorkerPartitionMismatch)]
    [InlineData("worker-purpose", ExecutionBindingFailure.WorkerPurposeMismatch)]
    [InlineData("offboarding", ExecutionBindingFailure.CustomerOffboarding)]
    [InlineData("standalone", ExecutionBindingFailure.StandaloneTenantMismatch)]
    public void OwnershipMismatchBlocksBeforeCredentialLookup(string mismatch, ExecutionBindingFailure expected)
    {
        var (envelope, stored, worker) = ValidBinding();
        (envelope, stored, worker) = mismatch switch
        {
            "tenant" => (envelope, stored with { Partition = new CustomerPartition("org-a", "tenant-b") }, worker),
            "purpose" => (envelope with { Purpose = ExecutionPurpose.Remediation }, stored, worker),
            "credential" => (envelope, stored with { CredentialReferenceId = "other-ref" }, worker),
            "revision" => (envelope, stored with { ConnectionRevision = "stale" }, worker),
            "connection" => (envelope, stored with { ConnectionEnabled = false }, worker),
            "adapter" => (envelope, stored, worker with { AllowedAdapterIds = ImmutableArray<string>.Empty }),
            "runtime" => (envelope, stored, worker with { Runtime = AdapterRuntime.Windows }),
            "worker-tenant" => (envelope, stored, worker with { Partition = new CustomerPartition("org-a", "tenant-b") }),
            "worker-purpose" => (envelope, stored, worker with { Purpose = ExecutionPurpose.Remediation }),
            "offboarding" => (envelope, stored with { CustomerOffboarding = true }, worker),
            "standalone" => (envelope, stored with { StandaloneTenantId = "tenant-b" }, worker),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

        var result = ExecutionCredentialAdmission.EvaluateBeforeCredentialLookup(envelope, stored, worker, Now);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public void MissingStoredJobIsRejectedBeforeCredentialLookup()
    {
        var (envelope, _, worker) = ValidBinding();

        var result = ExecutionCredentialAdmission.EvaluateBeforeCredentialLookup(envelope, null, worker, Now);

        Assert.Equal(ExecutionBindingFailure.IncompleteOwnership, result.Failure);
    }

    [Fact]
    public void ResolvedCredentialReferenceMustMatchStoredTenantPurposeAndConnectionRevision()
    {
        var (envelope, stored, _) = ValidBinding();
        var matching = new BoundCredentialReference(Customer, ExecutionPurpose.Assessment, "credential-ref-a", "connection-rev-1");
        var otherTenant = matching with { Partition = new CustomerPartition("org-a", "tenant-b") };
        var otherPurpose = matching with { Purpose = ExecutionPurpose.Remediation };
        var staleRevision = matching with { ConnectionRevision = "stale" };

        Assert.True(CredentialReferenceBindingPolicy.Matches(matching, envelope, stored));
        Assert.False(CredentialReferenceBindingPolicy.Matches(otherTenant, envelope, stored));
        Assert.False(CredentialReferenceBindingPolicy.Matches(otherPurpose, envelope, stored));
        Assert.False(CredentialReferenceBindingPolicy.Matches(staleRevision, envelope, stored));
    }

    [Fact]
    public void ExpiredQueueEnvelopeIsRejected()
    {
        var (envelope, stored, worker) = ValidBinding();

        var result = ExecutionBindingPolicy.Evaluate(envelope with { ExpiresAtUtc = Now }, stored, worker, Now);

        Assert.Equal(ExecutionBindingFailure.EnvelopeExpired, result.Failure);
    }

    private static (ServiceBusJobEnvelope Envelope, StoredJobOwnership Stored, WorkerDefinition Worker) ValidBinding()
    {
        var envelope = new ServiceBusJobEnvelope(
            1, Customer, "job-a", "connection-a", "connection-rev-1", ExecutionPurpose.Assessment,
            "credential-ref-a", "adapter-a", AdapterRuntime.Linux, Now.AddMinutes(5));
        var stored = new StoredJobOwnership(
            1, Customer, "job-a", "connection-a", "connection-rev-1", ExecutionPurpose.Assessment,
            "credential-ref-a", "adapter-a", true, false, false, "tenant-a");
        var worker = new WorkerDefinition(
            "worker-a", Customer, ExecutionPurpose.Assessment, AdapterRuntime.Linux,
            ImmutableArray.Create("adapter-a"));
        return (envelope, stored, worker);
    }
}