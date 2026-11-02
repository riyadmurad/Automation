using System.Collections.Immutable;

namespace M365.SecurityAssessment.Runtime;

public sealed record EmergencyAccountExclusion(string ControlId, string Rationale, string OwnerId);

public sealed record RecoveryExerciseRecord(
    DateTimeOffset TestedAtUtc,
    string ReviewerId,
    string Outcome,
    string EvidenceReference);

public sealed record EmergencyAccountRegistration(
    string DirectoryObjectId,
    string CustomerTenantId,
    string OwnerId,
    string IntendedRecoveryRole,
    ImmutableArray<EmergencyAccountExclusion> Exclusions,
    DateTimeOffset LastCustomerReviewUtc,
    RecoveryExerciseRecord? LatestExercise,
    string DisplayNameSnapshot);

public sealed record EmergencyRegistryRevision(
    string OrganizationId,
    string CustomerTenantId,
    string RevisionId,
    string CustomerConfirmedBy,
    DateTimeOffset CustomerConfirmedAtUtc,
    ImmutableArray<EmergencyAccountRegistration> Accounts);

public sealed record EmergencyAccountObservation(
    string DirectoryObjectId,
    string TenantId,
    bool Exists,
    bool Enabled,
    bool RequiredAccessPresent,
    bool EffectivePolicyScopeKnown,
    bool RecoveryPathUnblocked,
    bool MonitoringConfigured,
    bool AlertRoutingConfigured,
    bool TelemetryAvailable,
    bool AlertDeliveryVerified);

public enum EmergencyPreflightDecision
{
    Ready,
    RegistryMissing,
    RegistryInvalid,
    WrongTenant,
    EmptyRegistry,
    MissingOwnerOrRole,
    MissingAccount,
    DisabledAccount,
    MissingRecoveryAccess,
    StaleCustomerReview,
    MissingRecoveryExercise,
    StaleRecoveryExercise,
    UnknownEffectiveScope,
    RecoveryPathBlocked,
    MissingOrDuplicateObservation
}

public enum ProtectedAccountProperty
{
    DeleteAccount,
    DisableAccount,
    RemoveRecoveryRole,
    ChangeAuthenticationMethod,
    ChangeRecoveryContact,
    Other
}

public sealed record EmergencyReadinessView(
    string DirectoryObjectId,
    DateTimeOffset? LastReviewUtc,
    string? LastExerciseOutcome,
    bool MonitoringConfigured,
    bool AlertRoutingConfigured,
    bool AlertDeliveryVerified,
    string MonitoringStatus);

public static class EmergencyAccessPolicy
{
    public static EmergencyPreflightDecision Evaluate(
        EmergencyRegistryRevision? registry,
        CustomerPartition partition,
        IReadOnlyCollection<EmergencyAccountObservation> observations,
        DateTimeOffset nowUtc,
        TimeSpan maximumReviewAge)
    {
        if (registry is null)
        {
            return EmergencyPreflightDecision.RegistryMissing;
        }

        if (!string.Equals(registry.OrganizationId, partition.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(registry.CustomerTenantId, partition.CustomerTenantId, StringComparison.Ordinal))
        {
            return EmergencyPreflightDecision.WrongTenant;
        }

        if (registry.Accounts.IsDefaultOrEmpty)
        {
            return EmergencyPreflightDecision.EmptyRegistry;
        }

        if (string.IsNullOrWhiteSpace(registry.RevisionId) ||
            string.IsNullOrWhiteSpace(registry.CustomerConfirmedBy) ||
            registry.CustomerConfirmedAtUtc.Offset != TimeSpan.Zero ||
            registry.CustomerConfirmedAtUtc > nowUtc ||
            registry.Accounts.Select(account => account.DirectoryObjectId)
                .Distinct(StringComparer.Ordinal).Count() != registry.Accounts.Length)
        {
            return EmergencyPreflightDecision.RegistryInvalid;
        }

        foreach (var account in registry.Accounts)
        {
            if (!string.Equals(account.CustomerTenantId, partition.CustomerTenantId, StringComparison.Ordinal))
            {
                return EmergencyPreflightDecision.WrongTenant;
            }

            if (string.IsNullOrWhiteSpace(account.OwnerId) || string.IsNullOrWhiteSpace(account.IntendedRecoveryRole))
            {
                return EmergencyPreflightDecision.MissingOwnerOrRole;
            }

            var matches = observations.Where(observation => string.Equals(
                observation.DirectoryObjectId, account.DirectoryObjectId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                return EmergencyPreflightDecision.MissingOrDuplicateObservation;
            }

            var observation = matches[0];
            if (!string.Equals(observation.TenantId, partition.CustomerTenantId, StringComparison.Ordinal))
            {
                return EmergencyPreflightDecision.WrongTenant;
            }

            if (!observation.Exists)
            {
                return EmergencyPreflightDecision.MissingAccount;
            }

            if (!observation.Enabled)
            {
                return EmergencyPreflightDecision.DisabledAccount;
            }

            if (!observation.RequiredAccessPresent)
            {
                return EmergencyPreflightDecision.MissingRecoveryAccess;
            }

            if (account.LastCustomerReviewUtc.Offset != TimeSpan.Zero ||
                account.LastCustomerReviewUtc > nowUtc ||
                nowUtc - account.LastCustomerReviewUtc > maximumReviewAge)
            {
                return EmergencyPreflightDecision.StaleCustomerReview;
            }

            if (account.LatestExercise is null)
            {
                return EmergencyPreflightDecision.MissingRecoveryExercise;
            }

            if (account.LatestExercise.TestedAtUtc.Offset != TimeSpan.Zero ||
                account.LatestExercise.TestedAtUtc > nowUtc ||
                nowUtc - account.LatestExercise.TestedAtUtc > maximumReviewAge)
            {
                return EmergencyPreflightDecision.StaleRecoveryExercise;
            }

            if (!observation.EffectivePolicyScopeKnown)
            {
                return EmergencyPreflightDecision.UnknownEffectiveScope;
            }

            if (!observation.RecoveryPathUnblocked)
            {
                return EmergencyPreflightDecision.RecoveryPathBlocked;
            }
        }

        return EmergencyPreflightDecision.Ready;
    }

    public static bool IsProtectedMutation(
        EmergencyRegistryRevision registry,
        string customerTenantId,
        string resourceObjectId,
        ProtectedAccountProperty property) =>
        string.Equals(registry.CustomerTenantId, customerTenantId, StringComparison.Ordinal) &&
        property != ProtectedAccountProperty.Other &&
        registry.Accounts.Any(account => string.Equals(
            account.DirectoryObjectId, resourceObjectId, StringComparison.Ordinal));

    public static bool PlanUsesCurrentRegistry(ChangePlanRevision plan, EmergencyRegistryRevision registry) =>
        string.Equals(plan.OrganizationId, registry.OrganizationId, StringComparison.Ordinal) &&
        string.Equals(plan.CustomerTenantId, registry.CustomerTenantId, StringComparison.Ordinal) &&
        string.Equals(plan.EmergencyRegistryRevision, registry.RevisionId, StringComparison.Ordinal);

    public static ImmutableArray<EmergencyReadinessView> CreateReadinessView(
        EmergencyRegistryRevision registry,
        IReadOnlyDictionary<string, EmergencyAccountObservation> observations)
    {
        return registry.Accounts.Select(account =>
        {
            observations.TryGetValue(account.DirectoryObjectId, out var observation);
            var monitoringStatus = observation is null || !observation.TelemetryAvailable
                ? "unknown"
                : observation.AlertDeliveryVerified ? "delivery-verified"
                : observation.MonitoringConfigured && observation.AlertRoutingConfigured ? "configured-unverified"
                : "not-configured";
            return new EmergencyReadinessView(
                account.DirectoryObjectId,
                account.LastCustomerReviewUtc,
                account.LatestExercise?.Outcome,
                observation?.MonitoringConfigured == true,
                observation?.AlertRoutingConfigured == true,
                observation?.AlertDeliveryVerified == true,
                monitoringStatus);
        }).ToImmutableArray();
    }
}