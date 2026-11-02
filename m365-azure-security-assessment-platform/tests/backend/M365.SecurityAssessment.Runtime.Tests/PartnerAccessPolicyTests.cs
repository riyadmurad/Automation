using System.Collections.Immutable;
using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class PartnerAccessPolicyTests
{
    [Fact]
    public void LighthouseEligibilityUnknownWhenCurrentRequirementsOrFeatureEvidenceIsMissing()
    {
        var stale = EligibleInputs() with { RequirementsCurrent = false };
        var unknownFeatures = EligibleInputs() with { CustomerSupportsRequiredFeatures = null };

        Assert.Equal(CapabilityAvailability.Unknown, PartnerAccessPolicy.EvaluateEligibility(stale).Availability);
        Assert.Equal(CapabilityAvailability.Unknown, PartnerAccessPolicy.EvaluateEligibility(unknownFeatures).Availability);
    }

    [Fact]
    public void AllDocumentedEligibilityInputsMustPass()
    {
        var ineligible = EligibleInputs() with { CustomerRegionEligible = false };

        Assert.Equal(CapabilityAvailability.Unavailable, PartnerAccessPolicy.EvaluateEligibility(ineligible).Availability);
        Assert.Equal(CapabilityAvailability.Full, PartnerAccessPolicy.EvaluateEligibility(EligibleInputs()).Availability);
    }

    [Fact]
    public void AzureDelegationDoesNotAuthorizeMicrosoft365Assessment()
    {
        var inventory = Inventory(azureDelegations: [ValidAzureDelegation()]);

        var selected = PartnerAccessPolicy.TrySelectPath(
            inventory, PartnerAccessPath.AzureLighthouse, ServicePlane.Microsoft365,
            "Graph", "Directory.Read.All", RuntimeTestData.Now, out _);

        Assert.False(selected);
    }

    [Fact]
    public void ExpiredGdapDoesNotSilentlyFallBackToDirectConsent()
    {
        var inventory = Inventory(
            gdap: [new GdapRelationship("gdap-a", RuntimeTestData.Now, ImmutableArray.Create("Directory.Read.All"), false)],
            consents: [new DirectServiceConsent(
                "consent-a", "Graph", "tenant-a", ImmutableArray.Create("Directory.Read.All"), false)]);

        var gdapSelected = PartnerAccessPolicy.TrySelectPath(
            inventory, PartnerAccessPath.Gdap, ServicePlane.Microsoft365,
            "Graph", "Directory.Read.All", RuntimeTestData.Now, out _);
        var directSelected = PartnerAccessPolicy.TrySelectPath(
            inventory, PartnerAccessPath.DirectConsent, ServicePlane.Microsoft365,
            "Graph", "Directory.Read.All", RuntimeTestData.Now, out var directPath);

        Assert.False(gdapSelected);
        Assert.True(directSelected);
        Assert.Equal(PartnerAccessPath.DirectConsent, directPath!.Path);
        Assert.Equal("consent-a", directPath.RelationshipOrConsentId);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AzureDelegationBlocksOwnerDataActionsUnknownScopeOrUnverifiedRole(
        bool owner,
        bool dataActions,
        bool scopeCovered,
        bool preflightVerified)
    {
        var delegation = ValidAzureDelegation() with
        {
            IncludesOwner = owner,
            DataActions = dataActions ? ImmutableArray.Create("Microsoft.Storage/*") : [],
            TargetScopeCovered = scopeCovered,
            ScopeAndRolePreflightVerified = preflightVerified
        };

        var selected = PartnerAccessPolicy.TrySelectPath(
            Inventory(azureDelegations: [delegation]), PartnerAccessPath.AzureLighthouse,
            ServicePlane.Azure, "ARM", "Microsoft.Storage/storageAccounts/read",
            RuntimeTestData.Now, out _);

        Assert.False(selected);
    }

    [Fact]
    public void VerifiedScopedAzureDelegationCanSelectOnlyItsRequestedPath()
    {
        var delegation = ValidAzureDelegation();
        var inventory = Inventory(azureDelegations: [delegation]);

        Assert.True(PartnerAccessPolicy.TrySelectPath(
            inventory, PartnerAccessPath.AzureLighthouse, ServicePlane.Azure,
            "ARM", "Microsoft.Storage/storageAccounts/read", RuntimeTestData.Now, out var selected));
        Assert.Equal("delegation-a", selected!.RelationshipOrConsentId);
        Assert.False(PartnerAccessPolicy.TrySelectPath(
            inventory, PartnerAccessPath.Gdap, ServicePlane.Azure,
            "ARM", "Microsoft.Storage/storageAccounts/read", RuntimeTestData.Now, out _));
    }

    [Fact]
    public void PreviewConnectorIsDisabledByDefaultAndCannotFeedProduction()
    {
        Assert.False(PartnerAccessPolicy.IsPreviewConnectorAllowed(
            PreviewEvaluationPolicy.Default, RuntimeEnvironment.Test, false));
        Assert.False(PartnerAccessPolicy.IsPreviewConnectorAllowed(
            new PreviewEvaluationPolicy(true), RuntimeEnvironment.Production, false));
        Assert.False(PartnerAccessPolicy.IsPreviewConnectorAllowed(
            new PreviewEvaluationPolicy(true), RuntimeEnvironment.Development, true));
        Assert.True(PartnerAccessPolicy.IsPreviewConnectorAllowed(
            new PreviewEvaluationPolicy(true), RuntimeEnvironment.Test, false));
    }

    [Fact]
    public void PortalNavigationAcceptsOnlyNamedSupportedRoutes()
    {
        Assert.True(PartnerAccessPolicy.IsSupportedPortalNavigation("managed-tenants"));
        Assert.False(PartnerAccessPolicy.IsSupportedPortalNavigation("https://attacker.example"));
        Assert.False(PartnerAccessPolicy.IsSupportedPortalNavigation("private-api"));
    }

    private static LighthouseEligibilityInputs EligibleInputs() => new(
        "requirements-2026-11", true, true, true, true, true, true, true, ImmutableArray<string>.Empty);

    private static PartnerConnectionInventory Inventory(
        ImmutableArray<GdapRelationship> gdap = default,
        ImmutableArray<AzureDelegation> azureDelegations = default,
        ImmutableArray<DirectServiceConsent> consents = default) => new(
        "org-a",
        "tenant-a",
        new LighthouseEligibilityResult(CapabilityAvailability.Unknown, "requirements-2026-11", []),
        gdap.IsDefault ? [] : gdap,
        azureDelegations.IsDefault ? [] : azureDelegations,
        consents.IsDefault ? [] : consents);

    private static AzureDelegation ValidAzureDelegation() => new(
        "delegation-a",
        RuntimeTestData.Now.AddDays(10),
        ImmutableArray.Create("/subscriptions/sub-a"),
        ImmutableArray.Create("Reader"),
        ImmutableArray.Create("Microsoft.Storage/storageAccounts/read"),
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        false,
        true,
        true,
        false);
}