using System.Collections.Immutable;

namespace M365.SecurityAssessment.Runtime;

public enum RuntimeEnvironment
{
    Development,
    Test,
    Preproduction,
    Production
}

public enum ServicePlane
{
    Microsoft365,
    Azure
}

public enum PartnerAccessPath
{
    DirectConsent,
    Gdap,
    Microsoft365Lighthouse,
    AzureLighthouse
}

public enum CapabilityAvailability
{
    Full,
    Limited,
    Unavailable,
    Unknown
}

public sealed record GdapRelationship(
    string RelationshipId,
    DateTimeOffset ExpiresAtUtc,
    ImmutableArray<string> GrantedRoleIds,
    bool Revoked);

public sealed record AzureDelegation(
    string DelegationId,
    DateTimeOffset ExpiresAtUtc,
    ImmutableArray<string> ScopeIds,
    ImmutableArray<string> RoleIds,
    ImmutableArray<string> AllowedActions,
    ImmutableArray<string> DeniedActions,
    ImmutableArray<string> DataActions,
    bool IncludesOwner,
    bool ScopeAndRolePreflightVerified,
    bool TargetScopeCovered,
    bool Revoked);

public sealed record DirectServiceConsent(
    string ConsentId,
    string Service,
    string TenantId,
    ImmutableArray<string> GrantedPermissions,
    bool Revoked);

public sealed record LighthouseEligibilityInputs(
    string RequirementsRevision,
    bool RequirementsCurrent,
    bool PartnerEligible,
    bool CustomerRegionEligible,
    bool CustomerSizeEligible,
    bool SubscriptionEligible,
    bool DelegatedRelationshipEligible,
    bool? CustomerSupportsRequiredFeatures,
    ImmutableArray<string> Limitations);

public sealed record LighthouseEligibilityResult(
    CapabilityAvailability Availability,
    string RequirementsRevision,
    ImmutableArray<string> Reasons);

public sealed record PartnerConnectionInventory(
    string OrganizationId,
    string CustomerTenantId,
    LighthouseEligibilityResult Microsoft365Lighthouse,
    ImmutableArray<GdapRelationship> GdapRelationships,
    ImmutableArray<AzureDelegation> AzureDelegations,
    ImmutableArray<DirectServiceConsent> DirectConsents);

public sealed record SelectedAccessPath(
    PartnerAccessPath Path,
    ServicePlane Plane,
    string RelationshipOrConsentId,
    DateTimeOffset ValidUntilUtc,
    ImmutableArray<string> AuthorizedActions);

public sealed record PreviewEvaluationPolicy(bool Enabled)
{
    public static PreviewEvaluationPolicy Default { get; } = new(false);

    public bool CanRun(RuntimeEnvironment environment, bool forProductionFindingOrWrite) =>
        Enabled && environment != RuntimeEnvironment.Production && !forProductionFindingOrWrite;
}

public static class PartnerAccessPolicy
{
    private static readonly ImmutableHashSet<string> SupportedPortalPaths = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "managed-tenants",
        "service-health",
        "delegated-admin-relationships");

    public static LighthouseEligibilityResult EvaluateEligibility(LighthouseEligibilityInputs inputs)
    {
        if (!inputs.RequirementsCurrent || string.IsNullOrWhiteSpace(inputs.RequirementsRevision) ||
            inputs.CustomerSupportsRequiredFeatures is null)
        {
            return new LighthouseEligibilityResult(
                CapabilityAvailability.Unknown,
                inputs.RequirementsRevision,
                AppendReason(inputs.Limitations, "Current eligibility evidence is incomplete."));
        }

        if (!inputs.PartnerEligible || !inputs.CustomerRegionEligible || !inputs.CustomerSizeEligible ||
            !inputs.SubscriptionEligible || !inputs.DelegatedRelationshipEligible ||
            !inputs.CustomerSupportsRequiredFeatures.Value)
        {
            return new LighthouseEligibilityResult(
                CapabilityAvailability.Unavailable,
                inputs.RequirementsRevision,
                AppendReason(inputs.Limitations, "One or more documented eligibility requirements are not met."));
        }

        return new LighthouseEligibilityResult(CapabilityAvailability.Full, inputs.RequirementsRevision, inputs.Limitations);
    }

    public static bool TrySelectPath(
        PartnerConnectionInventory inventory,
        PartnerAccessPath requestedPath,
        ServicePlane plane,
        string service,
        string requiredAction,
        DateTimeOffset nowUtc,
        out SelectedAccessPath? selected)
    {
        selected = null;
        if (plane == ServicePlane.Azure)
        {
            if (requestedPath != PartnerAccessPath.AzureLighthouse)
            {
                return false;
            }

            foreach (var delegation in inventory.AzureDelegations)
            {
                if (delegation.Revoked || delegation.IncludesOwner ||
                    delegation.ExpiresAtUtc.Offset != TimeSpan.Zero || delegation.ExpiresAtUtc <= nowUtc ||
                    !delegation.ScopeAndRolePreflightVerified ||
                    !delegation.TargetScopeCovered ||
                    delegation.ScopeIds.IsDefaultOrEmpty || delegation.RoleIds.IsDefaultOrEmpty ||
                    !delegation.DataActions.IsDefaultOrEmpty ||
                    !delegation.AllowedActions.Contains(requiredAction, StringComparer.Ordinal) ||
                    delegation.DeniedActions.Contains(requiredAction, StringComparer.Ordinal))
                {
                    continue;
                }

                selected = new SelectedAccessPath(
                    PartnerAccessPath.AzureLighthouse,
                    plane,
                    delegation.DelegationId,
                    delegation.ExpiresAtUtc,
                    delegation.AllowedActions);
                return true;
            }
        }
        else
        {
            if (requestedPath == PartnerAccessPath.DirectConsent)
            {
                foreach (var consent in inventory.DirectConsents)
                {
                    if (!consent.Revoked &&
                        string.Equals(consent.TenantId, inventory.CustomerTenantId, StringComparison.Ordinal) &&
                        string.Equals(consent.Service, service, StringComparison.Ordinal) &&
                        consent.GrantedPermissions.Contains(requiredAction, StringComparer.Ordinal))
                    {
                        selected = new SelectedAccessPath(
                            PartnerAccessPath.DirectConsent,
                            plane,
                            consent.ConsentId,
                            DateTimeOffset.MaxValue,
                            consent.GrantedPermissions);
                        return true;
                    }
                }
            }
            else if (requestedPath == PartnerAccessPath.Gdap)
            {
                foreach (var relationship in inventory.GdapRelationships)
                {
                    if (!relationship.Revoked && relationship.ExpiresAtUtc.Offset == TimeSpan.Zero &&
                        relationship.ExpiresAtUtc > nowUtc &&
                        relationship.GrantedRoleIds.Contains(requiredAction, StringComparer.Ordinal))
                    {
                        selected = new SelectedAccessPath(
                            PartnerAccessPath.Gdap,
                            plane,
                            relationship.RelationshipId,
                            relationship.ExpiresAtUtc,
                            relationship.GrantedRoleIds);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool IsSupportedPortalNavigation(string path) => SupportedPortalPaths.Contains(path);

    public static bool IsPreviewConnectorAllowed(
        PreviewEvaluationPolicy policy,
        RuntimeEnvironment environment,
        bool productionFindingOrWrite) => policy.CanRun(environment, productionFindingOrWrite);

    private static ImmutableArray<string> AppendReason(ImmutableArray<string> reasons, string reason) =>
        (reasons.IsDefault ? ImmutableArray<string>.Empty : reasons).Add(reason);
}