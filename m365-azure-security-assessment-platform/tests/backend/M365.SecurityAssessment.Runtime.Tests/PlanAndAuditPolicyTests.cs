using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class PlanAndAuditPolicyTests
{
    [Fact]
    public void PlanHashIsStableForEquivalentChangeOrder()
    {
        var first = RuntimeTestData.Plan([RuntimeTestData.Change("operation-b"), RuntimeTestData.Change("operation-a")]);
        var second = RuntimeTestData.Plan([RuntimeTestData.Change("operation-a"), RuntimeTestData.Change("operation-b")]);

        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void PlanHashChangesForRequestedValuePurposeOrEmergencyRegistry()
    {
        var original = RuntimeTestData.Plan();
        var changedValue = RuntimeTestData.Plan([RuntimeTestData.Change(requested: "\"different\"")]);
        var changedPurpose = RuntimeTestData.Plan(purpose: ExecutionPurpose.Assessment);
        var changedRegistry = RuntimeTestData.Plan(emergencyRevision: "emergency-b");

        Assert.NotEqual(original.ContentHash, changedValue.ContentHash);
        Assert.NotEqual(original.ContentHash, changedPurpose.ContentHash);
        Assert.NotEqual(original.ContentHash, changedRegistry.ContentHash);
    }

    [Theory]
    [InlineData("other-organization", "tenant-a")]
    [InlineData("org-a", "tenant-b")]
    public void CrossCustomerTargetCannotEnterPlan(string organizationId, string tenantId)
    {
        var change = RuntimeTestData.Change(targetOrganizationId: organizationId, targetTenantId: tenantId);

        Assert.Throws<ArgumentException>(() => RuntimeTestData.Plan([change]));
    }

    [Fact]
    public void PlanRequiresValidJsonSettingValues()
    {
        Assert.Throws<ArgumentException>(() => RuntimeTestData.Plan([RuntimeTestData.Change(before: "not-json")]));
        Assert.Throws<ArgumentException>(() => RuntimeTestData.Plan([RuntimeTestData.Change(requested: "not-json")]));
    }

    [Fact]
    public void EditedPlanInvalidatesExactPlanApproval()
    {
        var plan = RuntimeTestData.Plan();
        var approval = RuntimeTestData.Approval(plan);
        var editedPlan = RuntimeTestData.Plan([RuntimeTestData.Change(requested: "\"edited\"")]);

        var decision = PlanApprovalPolicy.Evaluate(
            editedPlan, approval, new ApprovalPolicy(true), "authz-1", RuntimeTestData.Now);

        Assert.Equal(ApprovalDecision.WrongPlan, decision);
    }

    [Fact]
    public void ApprovalRequiresIndependentAuthorizedApproverAndCurrentAuthorization()
    {
        var plan = RuntimeTestData.Plan();
        var selfApproval = RuntimeTestData.Approval(plan, approverId: plan.RequesterId);
        var staleAuthorization = RuntimeTestData.Approval(plan, authorizationVersion: "authz-old");

        Assert.Equal(
            ApprovalDecision.SelfApproval,
            PlanApprovalPolicy.Evaluate(plan, selfApproval, new ApprovalPolicy(false), "authz-1", RuntimeTestData.Now));
        Assert.Equal(
            ApprovalDecision.AuthorizationChanged,
            PlanApprovalPolicy.Evaluate(plan, staleAuthorization, new ApprovalPolicy(false), "authz-1", RuntimeTestData.Now));
    }

    [Fact]
    public void ApprovalCannotSubstituteRequesterIdentity()
    {
        var plan = RuntimeTestData.Plan();
        var approval = RuntimeTestData.Approval(plan) with { RequesterId = "spoofed-requester" };

        Assert.Equal(ApprovalDecision.WrongPlan,
            PlanApprovalPolicy.Evaluate(plan, approval, new ApprovalPolicy(false), "authz-1", RuntimeTestData.Now));
    }

    [Fact]
    public void CustomerApprovalMustBeSeparateAndPresentWhenRequired()
    {
        var plan = RuntimeTestData.Plan();
        var missing = RuntimeTestData.Approval(plan, customerApprovalPresent: false, customerApproverId: null);
        var selfCustomerApproval = RuntimeTestData.Approval(plan, customerApproverId: plan.RequesterId);

        Assert.Equal(
            ApprovalDecision.CustomerApprovalRequired,
            PlanApprovalPolicy.Evaluate(plan, missing, new ApprovalPolicy(true), "authz-1", RuntimeTestData.Now));
        Assert.Equal(
            ApprovalDecision.CustomerApprovalRequired,
            PlanApprovalPolicy.Evaluate(plan, selfCustomerApproval, new ApprovalPolicy(true), "authz-1", RuntimeTestData.Now));
    }

    [Fact]
    public void ExpiredPlanAndApprovalAreRejected()
    {
        var plan = RuntimeTestData.Plan();
        var expiredApproval = RuntimeTestData.Approval(plan, expiresAt: RuntimeTestData.Now.AddMinutes(1));

        Assert.Equal(
            ApprovalDecision.Expired,
            PlanApprovalPolicy.Evaluate(plan, RuntimeTestData.Approval(plan), new ApprovalPolicy(false), "authz-1", RuntimeTestData.Now.AddHours(2)));
        Assert.Equal(
            ApprovalDecision.Expired,
            PlanApprovalPolicy.Evaluate(plan, expiredApproval, new ApprovalPolicy(false), "authz-1", RuntimeTestData.Now.AddMinutes(2)));
    }

    [Fact]
    public void AuditContractPreservesKnownUnknownAbsentAndRedactedValues()
    {
        var values = new[]
        {
            AuditValue.Known("false"), AuditValue.Unknown(), AuditValue.Absent(), AuditValue.Redacted()
        };

        Assert.Equal(4, values.Select(value => value.State).Distinct().Count());
        Assert.All(values, value => Assert.True(value.IsValid));
        Assert.False(AuditValue.Known("not-json").IsValid);
        Assert.Equal(AuditValueState.Redacted, SettingAuditContract.RedactIfSensitive("auth.password", values[0]).State);
        Assert.Equal(AuditValueState.Known, SettingAuditContract.RedactIfSensitive("policy.externalForwarding", values[0]).State);
    }

    [Fact]
    public void AuditIntentRequiresSettingLevelActorAndCorrelationFields()
    {
        var intent = new SettingAuditIntent(
            "event-a", RuntimeTestData.Now, "org-a", "tenant-a", "user-a", "requester-a", "approver-a",
            "workload-a", "Entra", "resource-a", "/policy/setting", AuditValue.Known("false"),
            AuditValue.Known("true"), "revision-a", "operation-a", "correlation-a");

        Assert.True(SettingAuditContract.IsValidIntent(intent));
        Assert.False(SettingAuditContract.IsValidIntent(intent with { SettingPath = string.Empty }));
        Assert.False(SettingAuditContract.IsValidIntent(intent with { TimestampUtc = RuntimeTestData.Now.ToOffset(TimeSpan.FromHours(1)) }));
    }
}