using System.Collections.Immutable;

namespace M365.SecurityAssessment.Runtime;

public enum ExecutionPurpose
{
    Assessment,
    Remediation
}

public sealed record CustomerPartition(string OrganizationId, string CustomerTenantId);

public enum AdapterRuntime
{
    Linux,
    Windows
}

public sealed record ServiceBusJobEnvelope(
    int ContractVersion,
    CustomerPartition Partition,
    string JobId,
    string ConnectionId,
    string ConnectionRevision,
    ExecutionPurpose Purpose,
    string CredentialReferenceId,
    string AdapterId,
    AdapterRuntime RequiredRuntime,
    DateTimeOffset ExpiresAtUtc);

public sealed record StoredJobOwnership(
    int ContractVersion,
    CustomerPartition Partition,
    string JobId,
    string ConnectionId,
    string ConnectionRevision,
    ExecutionPurpose Purpose,
    string CredentialReferenceId,
    string AdapterId,
    bool ConnectionEnabled,
    bool JobCancelled,
    bool CustomerOffboarding,
    string? StandaloneTenantId);

public sealed record WorkerDefinition(
    string WorkerDefinitionId,
    CustomerPartition Partition,
    ExecutionPurpose Purpose,
    AdapterRuntime Runtime,
    ImmutableArray<string> AllowedAdapterIds);

public enum ExecutionBindingFailure
{
    None,
    IncompleteOwnership,
    ContractVersionMismatch,
    TenantMismatch,
    JobMismatch,
    ConnectionMismatch,
    StaleConnectionRevision,
    PurposeMismatch,
    CredentialReferenceMismatch,
    AdapterMismatch,
    WorkerPartitionMismatch,
    WorkerPurposeMismatch,
    WorkerRuntimeMismatch,
    AdapterNotAllowed,
    ConnectionDisabled,
    JobCancelled,
    CustomerOffboarding,
    StandaloneTenantMismatch,
    EnvelopeExpired
}

public sealed record ExecutionBindingResult(bool IsValid, ExecutionBindingFailure Failure)
{
    public static ExecutionBindingResult Valid { get; } = new(true, ExecutionBindingFailure.None);
}

public static class ExecutionBindingPolicy
{
    public static ExecutionBindingResult Evaluate(
        ServiceBusJobEnvelope envelope,
        StoredJobOwnership stored,
        WorkerDefinition worker,
        DateTimeOffset nowUtc)
    {
        if (envelope.ContractVersion <= 0 || stored.ContractVersion <= 0 ||
            string.IsNullOrWhiteSpace(envelope.Partition.OrganizationId) ||
            string.IsNullOrWhiteSpace(envelope.Partition.CustomerTenantId) ||
            string.IsNullOrWhiteSpace(stored.Partition.OrganizationId) ||
            string.IsNullOrWhiteSpace(stored.Partition.CustomerTenantId) ||
            string.IsNullOrWhiteSpace(worker.Partition.OrganizationId) ||
            string.IsNullOrWhiteSpace(worker.Partition.CustomerTenantId) ||
            string.IsNullOrWhiteSpace(worker.WorkerDefinitionId) ||
            string.IsNullOrWhiteSpace(envelope.JobId) || string.IsNullOrWhiteSpace(stored.JobId) ||
            string.IsNullOrWhiteSpace(envelope.ConnectionId) || string.IsNullOrWhiteSpace(stored.ConnectionId) ||
            string.IsNullOrWhiteSpace(envelope.CredentialReferenceId) ||
            string.IsNullOrWhiteSpace(stored.CredentialReferenceId))
        {
            return Invalid(ExecutionBindingFailure.IncompleteOwnership);
        }

        if (envelope.ContractVersion != stored.ContractVersion)
        {
            return Invalid(ExecutionBindingFailure.ContractVersionMismatch);
        }

        if (!SamePartition(envelope.Partition, stored.Partition))
        {
            return Invalid(ExecutionBindingFailure.TenantMismatch);
        }

        if (!SamePartition(envelope.Partition, worker.Partition))
        {
            return Invalid(ExecutionBindingFailure.WorkerPartitionMismatch);
        }

        if (!string.Equals(envelope.JobId, stored.JobId, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.JobMismatch);
        }

        if (!string.Equals(envelope.ConnectionId, stored.ConnectionId, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.ConnectionMismatch);
        }

        if (!string.Equals(envelope.ConnectionRevision, stored.ConnectionRevision, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.StaleConnectionRevision);
        }

        if (envelope.Purpose != stored.Purpose)
        {
            return Invalid(ExecutionBindingFailure.PurposeMismatch);
        }

        if (envelope.Purpose != worker.Purpose)
        {
            return Invalid(ExecutionBindingFailure.WorkerPurposeMismatch);
        }

        if (!string.Equals(envelope.CredentialReferenceId, stored.CredentialReferenceId, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.CredentialReferenceMismatch);
        }

        if (!string.Equals(envelope.AdapterId, stored.AdapterId, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.AdapterMismatch);
        }

        if (envelope.RequiredRuntime != worker.Runtime)
        {
            return Invalid(ExecutionBindingFailure.WorkerRuntimeMismatch);
        }

        if (!worker.AllowedAdapterIds.Contains(envelope.AdapterId, StringComparer.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.AdapterNotAllowed);
        }

        if (!stored.ConnectionEnabled)
        {
            return Invalid(ExecutionBindingFailure.ConnectionDisabled);
        }

        if (stored.JobCancelled)
        {
            return Invalid(ExecutionBindingFailure.JobCancelled);
        }

        if (stored.CustomerOffboarding)
        {
            return Invalid(ExecutionBindingFailure.CustomerOffboarding);
        }

        if (stored.StandaloneTenantId is not null &&
            !string.Equals(stored.StandaloneTenantId, stored.Partition.CustomerTenantId, StringComparison.Ordinal))
        {
            return Invalid(ExecutionBindingFailure.StandaloneTenantMismatch);
        }

        if (nowUtc.Offset != TimeSpan.Zero || envelope.ExpiresAtUtc <= nowUtc)
        {
            return Invalid(ExecutionBindingFailure.EnvelopeExpired);
        }

        return ExecutionBindingResult.Valid;
    }

    private static ExecutionBindingResult Invalid(ExecutionBindingFailure failure) => new(false, failure);

    private static bool SamePartition(CustomerPartition first, CustomerPartition second) =>
        string.Equals(first.OrganizationId, second.OrganizationId, StringComparison.Ordinal) &&
        string.Equals(first.CustomerTenantId, second.CustomerTenantId, StringComparison.Ordinal);
}

public interface IStoredJobOwnershipReader
{
    ValueTask<StoredJobOwnership?> FindAsync(string jobId, CancellationToken cancellationToken);
}

public sealed record BoundCredentialReference(
    CustomerPartition Partition,
    ExecutionPurpose Purpose,
    string CredentialReferenceId,
    string ConnectionRevision);

public interface IStoredCredentialReferenceResolver
{
    ValueTask<BoundCredentialReference?> ResolveReferenceAsync(
        CustomerPartition partition,
        ExecutionPurpose purpose,
        string credentialReferenceId,
        CancellationToken cancellationToken);
}

public static class ExecutionCredentialAdmission
{
    public static ExecutionBindingResult EvaluateBeforeCredentialLookup(
        ServiceBusJobEnvelope envelope,
        StoredJobOwnership? stored,
        WorkerDefinition worker,
        DateTimeOffset nowUtc) => stored is null
            ? new ExecutionBindingResult(false, ExecutionBindingFailure.IncompleteOwnership)
            : ExecutionBindingPolicy.Evaluate(envelope, stored, worker, nowUtc);
}

public static class CredentialReferenceBindingPolicy
{
    public static bool Matches(
        BoundCredentialReference? resolved,
        ServiceBusJobEnvelope envelope,
        StoredJobOwnership stored) =>
        resolved is not null &&
        string.Equals(resolved.Partition.OrganizationId, stored.Partition.OrganizationId, StringComparison.Ordinal) &&
        string.Equals(resolved.Partition.CustomerTenantId, stored.Partition.CustomerTenantId, StringComparison.Ordinal) &&
        resolved.Partition == envelope.Partition &&
        resolved.Purpose == stored.Purpose &&
        resolved.Purpose == envelope.Purpose &&
        string.Equals(resolved.CredentialReferenceId, stored.CredentialReferenceId, StringComparison.Ordinal) &&
        string.Equals(resolved.CredentialReferenceId, envelope.CredentialReferenceId, StringComparison.Ordinal) &&
        string.Equals(resolved.ConnectionRevision, stored.ConnectionRevision, StringComparison.Ordinal);
}

public sealed record RetryPolicy(int MaximumRetries, TimeSpan MaximumBackoff, TimeSpan MaximumJobAge)
{
    public static RetryPolicy DevelopmentDefault { get; } = new(5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30));
}

public sealed record RetryDecision(bool ShouldRetry, DateTimeOffset? RetryAtUtc, string Reason);

public static class BoundedRetryPolicy
{
    public static RetryDecision Evaluate(
        RetryPolicy policy,
        int retriesAlreadyUsed,
        DateTimeOffset firstAttemptUtc,
        DateTimeOffset nowUtc,
        TimeSpan? retryAfter)
    {
        if (nowUtc.Offset != TimeSpan.Zero || firstAttemptUtc.Offset != TimeSpan.Zero ||
            retriesAlreadyUsed < 0 || policy.MaximumRetries < 0 ||
            policy.MaximumBackoff <= TimeSpan.Zero || policy.MaximumJobAge <= TimeSpan.Zero)
        {
            return new RetryDecision(false, null, "Retry policy or timestamps are invalid.");
        }

        if (retriesAlreadyUsed >= policy.MaximumRetries)
        {
            return new RetryDecision(false, null, "Retry limit exhausted.");
        }

        if (nowUtc - firstAttemptUtc >= policy.MaximumJobAge)
        {
            return new RetryDecision(false, null, "Job age limit exhausted.");
        }

        var exponent = Math.Min(retriesAlreadyUsed, 30);
        var seconds = Math.Min(policy.MaximumBackoff.TotalSeconds, Math.Pow(2, exponent));
        var delay = TimeSpan.FromSeconds(seconds);
        if (retryAfter is { } serverDelay && serverDelay > delay)
        {
            delay = serverDelay;
        }

        if (nowUtc.Add(delay) - firstAttemptUtc > policy.MaximumJobAge)
        {
            return new RetryDecision(false, null, "Retry-After exceeds the bounded job deadline.");
        }

        return new RetryDecision(true, nowUtc.Add(delay), "Transient failure; retry is bounded and Retry-After is honored.");
    }
}

public sealed record TenantConcurrencyLimit(int MaximumActiveJobsPerCustomer);

public interface ITenantExecutionQuotaStore
{
    ValueTask<bool> TryAcquireAsync(
        CustomerPartition partition,
        ExecutionPurpose purpose,
        string jobId,
        TenantConcurrencyLimit limit,
        CancellationToken cancellationToken);

    ValueTask ReleaseAsync(CustomerPartition partition, string jobId, CancellationToken cancellationToken);
}

public sealed record LiveContractTestAuthorization(
    string AuthorizationId,
    string CustomerTenantId,
    ImmutableHashSet<string> DisposableResourceIds,
    string AuthorizedBy,
    DateTimeOffset ExpiresAtUtc,
    bool ExplicitOptIn,
    bool ResourcesAreDisposable);

public enum LiveContractTestDecision
{
    Authorized,
    MissingAuthorization,
    CiExecutionDenied,
    OptInRequired,
    NonDisposableScope,
    TenantMismatch,
    ResourceOutOfScope,
    Expired,
    InvalidAuthorization
}

public static class LiveContractTestPolicy
{
    public static LiveContractTestDecision Evaluate(
        LiveContractTestAuthorization? authorization,
        string customerTenantId,
        IEnumerable<string> targetResourceIds,
        DateTimeOffset nowUtc,
        bool runningInCi)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            return LiveContractTestDecision.InvalidAuthorization;
        }

        if (runningInCi)
        {
            return LiveContractTestDecision.CiExecutionDenied;
        }

        if (authorization is null)
        {
            return LiveContractTestDecision.MissingAuthorization;
        }

        if (!authorization.ExplicitOptIn)
        {
            return LiveContractTestDecision.OptInRequired;
        }

        if (!authorization.ResourcesAreDisposable)
        {
            return LiveContractTestDecision.NonDisposableScope;
        }

        if (!string.Equals(authorization.CustomerTenantId, customerTenantId, StringComparison.Ordinal))
        {
            return LiveContractTestDecision.TenantMismatch;
        }

        if (authorization.ExpiresAtUtc.Offset != TimeSpan.Zero || authorization.ExpiresAtUtc <= nowUtc)
        {
            return LiveContractTestDecision.Expired;
        }

        if (string.IsNullOrWhiteSpace(authorization.AuthorizationId) ||
            string.IsNullOrWhiteSpace(authorization.AuthorizedBy) ||
            authorization.DisposableResourceIds.IsEmpty)
        {
            return LiveContractTestDecision.InvalidAuthorization;
        }

        var targets = targetResourceIds.ToArray();
        return targets.Length > 0 && targets.All(authorization.DisposableResourceIds.Contains)
            ? LiveContractTestDecision.Authorized
            : LiveContractTestDecision.ResourceOutOfScope;
    }
}