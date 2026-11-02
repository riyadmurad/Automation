using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

public sealed class RuntimeReliabilityPolicyTests
{
    [Fact]
    public void RetryUsesBoundedExponentialBackoffAndHonorsRetryAfter()
    {
        var firstAttempt = RuntimeTestData.Now;
        var serverDelay = BoundedRetryPolicy.Evaluate(
            RetryPolicy.DevelopmentDefault, 2, firstAttempt, firstAttempt.AddMinutes(1), TimeSpan.FromMinutes(4));
        var maxRetries = BoundedRetryPolicy.Evaluate(
            RetryPolicy.DevelopmentDefault, 5, firstAttempt, firstAttempt, null);
        var tooOld = BoundedRetryPolicy.Evaluate(
            RetryPolicy.DevelopmentDefault, 0, firstAttempt, firstAttempt.AddMinutes(29), TimeSpan.FromMinutes(2));

        Assert.True(serverDelay.ShouldRetry);
        Assert.Equal(firstAttempt.AddMinutes(5), serverDelay.RetryAtUtc);
        Assert.False(maxRetries.ShouldRetry);
        Assert.False(tooOld.ShouldRetry);
    }

    [Theory]
    [InlineData(AdapterRuntime.Windows, AdapterRuntime.Linux, false)]
    [InlineData(AdapterRuntime.Windows, AdapterRuntime.Windows, true)]
    [InlineData(AdapterRuntime.Linux, AdapterRuntime.Linux, true)]
    public void RuntimeGuardBlocksWindowsOnlyJobOnLinux(AdapterRuntime required, AdapterRuntime available, bool expected)
    {
        Assert.Equal(expected, AdapterRuntimePolicy.Evaluate(required, available).Allowed);
    }

    [Fact]
    public void OperationCannotQualifyWithoutEveryFeasibilityEvidenceItem()
    {
        var complete = new OperationFeasibilityEvidence(
            "operation-a", "source-rev-a", true, true, true, true, true,
            TimeSpan.FromMinutes(2), true, true);

        Assert.True(OperationQualificationPolicy.IsFeasibilityComplete(complete));
        Assert.False(OperationQualificationPolicy.IsFeasibilityComplete(complete with { ConditionalWriteVerified = false }));
        Assert.False(OperationQualificationPolicy.IsFeasibilityComplete(complete with { RecoveryDocumented = false }));
        Assert.False(OperationQualificationPolicy.IsFeasibilityComplete(complete with { FixtureTestsPassed = false }));
    }

    [Fact]
    public void LiveContractTestIsBlockedWithoutCurrentNamedDisposableAuthorizationAndNeverRunsInCi()
    {
        var valid = new LiveContractTestAuthorization(
            "authorization-a", "tenant-a", ["resource-a"], "customer-approver",
            RuntimeTestData.Now.AddMinutes(30), true, true);

        Assert.Equal(LiveContractTestDecision.MissingAuthorization,
            LiveContractTestPolicy.Evaluate(null, "tenant-a", ["resource-a"], RuntimeTestData.Now, false));
        Assert.Equal(LiveContractTestDecision.CiExecutionDenied,
            LiveContractTestPolicy.Evaluate(valid, "tenant-a", ["resource-a"], RuntimeTestData.Now, true));
        Assert.Equal(LiveContractTestDecision.ResourceOutOfScope,
            LiveContractTestPolicy.Evaluate(valid, "tenant-a", ["resource-b"], RuntimeTestData.Now, false));
        Assert.Equal(LiveContractTestDecision.InvalidAuthorization,
            LiveContractTestPolicy.Evaluate(valid with { DisposableResourceIds = [] }, "tenant-a", ["resource-a"], RuntimeTestData.Now, false));
        Assert.Equal(LiveContractTestDecision.ResourceOutOfScope,
            LiveContractTestPolicy.Evaluate(valid, "tenant-a", [], RuntimeTestData.Now, false));
        Assert.Equal(LiveContractTestDecision.Authorized,
            LiveContractTestPolicy.Evaluate(valid, "tenant-a", ["resource-a"], RuntimeTestData.Now, false));
    }
}