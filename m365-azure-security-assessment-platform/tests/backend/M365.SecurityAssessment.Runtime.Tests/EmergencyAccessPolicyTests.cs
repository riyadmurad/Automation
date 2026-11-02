using System.Collections.Immutable;
using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class EmergencyAccessPolicyTests
{
    private static readonly TimeSpan ReviewAge = TimeSpan.FromDays(90);

    [Fact]
    public void ImmutableObjectIdRemainsProtectedAfterDisplayNameChanges()
    {
        var renamed = RuntimeTestData.EmergencyAccount(displayName: "Renamed recovery account");
        var registry = RuntimeTestData.EmergencyRegistry(account: renamed);

        Assert.True(EmergencyAccessPolicy.IsProtectedMutation(
            registry, "tenant-a", renamed.DirectoryObjectId, ProtectedAccountProperty.ChangeAuthenticationMethod));
        Assert.False(EmergencyAccessPolicy.IsProtectedMutation(
            registry, "tenant-a", "different-object", ProtectedAccountProperty.DisableAccount));
    }

    [Fact]
    public void ReadyAccountPassesAndRemainsVisibleWithItsControlSpecificException()
    {
        var account = RuntimeTestData.EmergencyAccount();
        var registry = RuntimeTestData.EmergencyRegistry(account: account);
        var observation = RuntimeTestData.EmergencyObservation();

        var decision = EmergencyAccessPolicy.Evaluate(
            registry, RuntimeTestData.Customer, [observation], RuntimeTestData.Now, ReviewAge);

        Assert.Equal(EmergencyPreflightDecision.Ready, decision);
        Assert.Equal("ENT-001", Assert.Single(account.Exclusions).ControlId);
        Assert.Single(EmergencyAccessPolicy.CreateReadinessView(
            registry, new Dictionary<string, EmergencyAccountObservation> { [account.DirectoryObjectId] = observation }));
    }

    [Theory]
    [InlineData("deleted", EmergencyPreflightDecision.MissingAccount)]
    [InlineData("disabled", EmergencyPreflightDecision.DisabledAccount)]
    [InlineData("no-access", EmergencyPreflightDecision.MissingRecoveryAccess)]
    [InlineData("wrong-tenant", EmergencyPreflightDecision.WrongTenant)]
    [InlineData("overlap-unknown", EmergencyPreflightDecision.UnknownEffectiveScope)]
    [InlineData("recovery-blocked", EmergencyPreflightDecision.RecoveryPathBlocked)]
    public void UnsafeOrUnknownRecoveryEvidenceBlocks(string state, EmergencyPreflightDecision expected)
    {
        var observation = RuntimeTestData.EmergencyObservation() with
        {
            Exists = state != "deleted",
            Enabled = state != "disabled",
            RequiredAccessPresent = state != "no-access",
            TenantId = state == "wrong-tenant" ? "tenant-b" : "tenant-a",
            EffectivePolicyScopeKnown = state != "overlap-unknown",
            RecoveryPathUnblocked = state != "recovery-blocked"
        };

        var result = EmergencyAccessPolicy.Evaluate(
            RuntimeTestData.EmergencyRegistry(), RuntimeTestData.Customer, [observation], RuntimeTestData.Now, ReviewAge);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MissingRegistryAndStaleReviewFailClosed()
    {
        var stale = RuntimeTestData.EmergencyAccount() with { LastCustomerReviewUtc = RuntimeTestData.Now.AddDays(-91) };

        Assert.Equal(
            EmergencyPreflightDecision.RegistryMissing,
            EmergencyAccessPolicy.Evaluate(null, RuntimeTestData.Customer, [], RuntimeTestData.Now, ReviewAge));
        Assert.Equal(
            EmergencyPreflightDecision.StaleCustomerReview,
            EmergencyAccessPolicy.Evaluate(
                RuntimeTestData.EmergencyRegistry(account: stale), RuntimeTestData.Customer,
                [RuntimeTestData.EmergencyObservation()], RuntimeTestData.Now, ReviewAge));
    }

    [Fact]
    public void RegistryRequiresCustomerConfirmationAndUniqueImmutableIds()
    {
        var registry = RuntimeTestData.EmergencyRegistry();
        var unconfirmed = registry with { CustomerConfirmedBy = string.Empty };
        var duplicateIds = registry with { Accounts = ImmutableArray.Create(registry.Accounts[0], registry.Accounts[0]) };

        Assert.Equal(EmergencyPreflightDecision.RegistryInvalid,
            EmergencyAccessPolicy.Evaluate(unconfirmed, RuntimeTestData.Customer,
                [RuntimeTestData.EmergencyObservation()], RuntimeTestData.Now, ReviewAge));
        Assert.Equal(EmergencyPreflightDecision.RegistryInvalid,
            EmergencyAccessPolicy.Evaluate(duplicateIds, RuntimeTestData.Customer,
                [RuntimeTestData.EmergencyObservation()], RuntimeTestData.Now, ReviewAge));
    }

    [Fact]
    public void StaleRecoveryExerciseBlocksLockoutSensitivePlan()
    {
        var stale = RuntimeTestData.EmergencyAccount() with
        {
            LatestExercise = new RecoveryExerciseRecord(
                RuntimeTestData.Now.AddDays(-91), "reviewer-a", "passed", "evidence-a")
        };

        var result = EmergencyAccessPolicy.Evaluate(
            RuntimeTestData.EmergencyRegistry(account: stale), RuntimeTestData.Customer,
            [RuntimeTestData.EmergencyObservation()], RuntimeTestData.Now, ReviewAge);

        Assert.Equal(EmergencyPreflightDecision.StaleRecoveryExercise, result);
    }

    [Theory]
    [InlineData(ProtectedAccountProperty.DeleteAccount)]
    [InlineData(ProtectedAccountProperty.DisableAccount)]
    [InlineData(ProtectedAccountProperty.RemoveRecoveryRole)]
    [InlineData(ProtectedAccountProperty.ChangeAuthenticationMethod)]
    [InlineData(ProtectedAccountProperty.ChangeRecoveryContact)]
    public void OrdinaryRemediationCannotChangeProtectedProperties(ProtectedAccountProperty property)
    {
        Assert.True(EmergencyAccessPolicy.IsProtectedMutation(
            RuntimeTestData.EmergencyRegistry(), "tenant-a", "object-a", property));
    }

    [Fact]
    public void RegistryRevisionChangeInvalidatesDependentPlan()
    {
        var plan = RuntimeTestData.Plan(emergencyRevision: "emergency-a");
        var changedRegistry = RuntimeTestData.EmergencyRegistry(revision: "emergency-b");

        Assert.False(EmergencyAccessPolicy.PlanUsesCurrentRegistry(plan, changedRegistry));
    }

    [Fact]
    public void UnavailableMonitoringIsUnknownNotHealthyAndStoresNoCredentialFields()
    {
        var registry = RuntimeTestData.EmergencyRegistry();
        var observation = RuntimeTestData.EmergencyObservation() with
        {
            TelemetryAvailable = false,
            AlertDeliveryVerified = false
        };

        var view = Assert.Single(EmergencyAccessPolicy.CreateReadinessView(
            registry, new Dictionary<string, EmergencyAccountObservation> { ["object-a"] = observation }));

        Assert.Equal("unknown", view.MonitoringStatus);
        Assert.DoesNotContain("Password", string.Join(',', typeof(EmergencyReadinessView).GetProperties().Select(property => property.Name)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecoveryCode", string.Join(',', typeof(EmergencyReadinessView).GetProperties().Select(property => property.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguredMonitoringWithoutDeliveryVerificationRemainsUnverified()
    {
        var observation = RuntimeTestData.EmergencyObservation() with { AlertDeliveryVerified = false };
        var view = Assert.Single(EmergencyAccessPolicy.CreateReadinessView(
            RuntimeTestData.EmergencyRegistry(),
            new Dictionary<string, EmergencyAccountObservation> { ["object-a"] = observation }));

        Assert.True(view.MonitoringConfigured);
        Assert.True(view.AlertRoutingConfigured);
        Assert.False(view.AlertDeliveryVerified);
        Assert.Equal("configured-unverified", view.MonitoringStatus);

        var notConfigured = observation with { MonitoringConfigured = false, AlertRoutingConfigured = false };
        var unconfiguredView = Assert.Single(EmergencyAccessPolicy.CreateReadinessView(
            RuntimeTestData.EmergencyRegistry(),
            new Dictionary<string, EmergencyAccountObservation> { ["object-a"] = notConfigured }));
        Assert.Equal("not-configured", unconfiguredView.MonitoringStatus);
    }
}