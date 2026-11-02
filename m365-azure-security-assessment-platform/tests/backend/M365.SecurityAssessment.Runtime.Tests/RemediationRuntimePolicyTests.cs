using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class RemediationRuntimePolicyTests
{
    [Fact]
    public void FreshExactPreflightPassesButRemediationRemainsUnqualifiedAndNotAllowlisted()
    {
        var plan = RuntimeTestData.Plan();
        var change = Assert.Single(plan.Changes);
        var request = new RemediationAdmissionRequest(
            plan,
            change,
            RuntimeTestData.Approval(plan),
            new ApprovalPolicy(true),
            "authz-1",
            RuntimeTestData.Preflight(change),
            RuntimeTestData.Now,
            TimeSpan.FromMinutes(2));

        var result = RemediationAdmissionPolicy.Evaluate(request);

        Assert.False(result.Admitted);
        Assert.Equal(RemediationAdmissionFailure.OperationNotQualified, result.Failure);
        Assert.Empty(MutationAllowlist.EnabledOperationIds);
        Assert.False(MutationAllowlist.IsEnabled(change.OperationId));

        var completeQualification = new OperationFeasibilityEvidence(
            change.OperationId, "source-revision", true, true, true, true, true,
            TimeSpan.FromMinutes(2), true, true);
        var qualifiedButDisabled = RemediationAdmissionPolicy.Evaluate(request with
        {
            QualificationEvidence = completeQualification
        });
        Assert.Equal(RemediationAdmissionFailure.OperationNotAllowlisted, qualifiedButDisabled.Failure);
        var wrongOperationQualification = RemediationAdmissionPolicy.Evaluate(request with
        {
            QualificationEvidence = completeQualification with { OperationId = "other-operation" }
        });
        Assert.Equal(RemediationAdmissionFailure.OperationNotQualified, wrongOperationQualification.Failure);
    }

    [Theory]
    [InlineData("audit", PreflightBlocker.AuditUnavailable)]
    [InlineData("stop-unknown", PreflightBlocker.StopFlagUnknown)]
    [InlineData("stopped", PreflightBlocker.StopActive)]
    [InlineData("permissions", PreflightBlocker.MissingPermission)]
    [InlineData("scope", PreflightBlocker.UnknownOrIncompleteScope)]
    [InlineData("connection", PreflightBlocker.RevokedOrStaleConnection)]
    [InlineData("registry", PreflightBlocker.EmergencyRegistryChanged)]
    [InlineData("etag", PreflightBlocker.ETagMissingOrChanged)]
    [InlineData("target", PreflightBlocker.TargetChanged)]
    [InlineData("old", PreflightBlocker.EvidenceExpired)]
    public void MissingOrChangedPreflightEvidenceBlocksBeforeWrite(string defect, PreflightBlocker blocker)
    {
        var plan = RuntimeTestData.Plan();
        var change = Assert.Single(plan.Changes);
        var evidence = RuntimeTestData.Preflight(change);
        evidence = defect switch
        {
            "audit" => evidence with { AuditStoreAvailable = false },
            "stop-unknown" => evidence with { StopFlagKnown = false },
            "stopped" => evidence with { StopActive = true },
            "permissions" => evidence with { PermissionKnownAndSufficient = false },
            "scope" => evidence with { TargetScopeComplete = false },
            "connection" => evidence with { ConnectionCurrent = false },
            "registry" => evidence with { CurrentEmergencyRegistryRevision = "changed" },
            "etag" => evidence with { ETag = null },
            "target" => evidence with { ObservedValueJson = "\"changed\"" },
            "old" => evidence with { CollectedAtUtc = RuntimeTestData.Now.AddMinutes(-3) },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        var result = OperationPreflightPolicy.Evaluate(plan, change, evidence, RuntimeTestData.Now, TimeSpan.FromMinutes(2));

        Assert.False(result.Allowed);
        Assert.Contains(blocker, result.Blockers);
    }

    [Fact]
    public void LockoutSensitiveChangeRequiresEmergencyReadiness()
    {
        var change = RuntimeTestData.Change(lockoutSensitive: true);
        var plan = RuntimeTestData.Plan([change]);
        var evidence = RuntimeTestData.Preflight(change) with
        {
            EmergencyAccessDecision = EmergencyPreflightDecision.UnknownEffectiveScope
        };

        var result = OperationPreflightPolicy.Evaluate(plan, change, evidence, RuntimeTestData.Now, TimeSpan.FromMinutes(2));

        Assert.Contains(PreflightBlocker.EmergencyAccessNotReady, result.Blockers);
    }

    [Fact]
    public void PreflightRejectsChangeNotBoundToPlanAndWrongPurpose()
    {
        var plan = RuntimeTestData.Plan();
        var unplanned = RuntimeTestData.Change("other-operation");
        var request = new RemediationAdmissionRequest(
            plan, unplanned, RuntimeTestData.Approval(plan), new ApprovalPolicy(false), "authz-1",
            RuntimeTestData.Preflight(unplanned), RuntimeTestData.Now, TimeSpan.FromMinutes(2));

        Assert.Equal(RemediationAdmissionFailure.PlanHashMismatch, RemediationAdmissionPolicy.Evaluate(request).Failure);

        var assessmentPlan = RuntimeTestData.Plan(purpose: ExecutionPurpose.Assessment);
        var assessmentRequest = request with
        {
            Plan = assessmentPlan,
            Change = Assert.Single(assessmentPlan.Changes),
            Approval = RuntimeTestData.Approval(assessmentPlan)
        };
        Assert.Equal(RemediationAdmissionFailure.PurposeMismatch, RemediationAdmissionPolicy.Evaluate(assessmentRequest).Failure);
    }

    [Fact]
    public void DuplicateVerifiedOperationReturnsStoredOutcomeAndUnknownRequiresReadReconciliation()
    {
        Assert.Equal(DuplicateOperationDecision.StartNew, OperationIdempotencyPolicy.Evaluate(null));
        Assert.Equal(DuplicateOperationDecision.ReturnStoredOutcome, OperationIdempotencyPolicy.Evaluate(OperationState.Verified));
        Assert.Equal(DuplicateOperationDecision.ReconcileRemoteState, OperationIdempotencyPolicy.Evaluate(OperationState.Unknown));
        Assert.Equal(DuplicateOperationDecision.ReconcileRemoteState, OperationIdempotencyPolicy.Evaluate(OperationState.PendingVerification));
        Assert.Equal(DuplicateOperationDecision.ContinueExistingOperation, OperationIdempotencyPolicy.Evaluate(OperationState.Executing));
    }

    [Fact]
    public void UncertainWriteRequiresRemoteReadBeforeRetryCanBeConsidered()
    {
        var change = RuntimeTestData.Change();

        Assert.Equal(UncertainWriteDisposition.RemainUnknown,
            UncertainWriteReconciliationPolicy.Evaluate(change, false, null));
        Assert.Equal(UncertainWriteDisposition.VerifyObservedRequestedValue,
            UncertainWriteReconciliationPolicy.Evaluate(change, true, change.RequestedValueJson));
        Assert.Equal(UncertainWriteDisposition.RetryMayBeConsideredAfterFreshPreflight,
            UncertainWriteReconciliationPolicy.Evaluate(change, true, change.BeforeValueJson));
        Assert.Equal(UncertainWriteDisposition.ConflictRequiresNewPlan,
            UncertainWriteReconciliationPolicy.Evaluate(change, true, "\"external-edit\""));
    }

    [Fact]
    public void UnknownOperationCannotReturnToExecutionWithoutReconciliation()
    {
        var context = new OperationTransitionContext(false, false, false, false);

        Assert.Equal(OperationState.Unknown,
            OperationStateMachine.Transition(OperationState.Unknown, OperationEvent.BeginExecution, context));
        Assert.Equal(OperationState.PendingVerification,
            OperationStateMachine.Transition(OperationState.Unknown, OperationEvent.RemoteReadCompleted,
                context with { RemoteReadCompleted = true }));
    }

    [Fact]
    public void StopBlocksNextWriteAndPreservesEarlierSuccessAsPartial()
    {
        var state = OperationStateMachine.Transition(
            OperationState.Queued, OperationEvent.BeginPreflight,
            new OperationTransitionContext(false, true, true, false));
        var partial = OperationStateMachine.Aggregate([OperationState.Verified, OperationState.Cancelled]);

        Assert.Equal(OperationState.Blocked, state);
        Assert.Equal(OperationState.PartiallyCompleted, partial);
    }

    [Fact]
    public void InFlightUncertaintyIsNotReplacedByPlanExpiry()
    {
        var state = OperationStateMachine.Transition(
            OperationState.Unknown, OperationEvent.StopRequested,
            new OperationTransitionContext(false, true, true, true));

        Assert.Equal(OperationState.Unknown, state);
    }
}