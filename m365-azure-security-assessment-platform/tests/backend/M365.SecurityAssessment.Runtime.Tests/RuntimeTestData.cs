using System.Collections.Immutable;
using M365.SecurityAssessment.Runtime;

namespace M365.SecurityAssessment.Runtime.Tests;

internal static class RuntimeTestData
{
    public static DateTimeOffset Now { get; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    public static CustomerPartition Customer { get; } = new("org-a", "tenant-a");

    public static PlannedSettingChange Change(
        string operationId = "operation-a",
        string before = "\"before\"",
        string requested = "\"requested\"",
        string targetOrganizationId = "org-a",
        string targetTenantId = "tenant-a",
        string etag = "etag-1",
        bool lockoutSensitive = false) => new(
        operationId,
        targetOrganizationId,
        targetTenantId,
        "Entra",
        "resource-a",
        "/policies/sample",
        before,
        requested,
        etag,
        lockoutSensitive);

    public static ChangePlanRevision Plan(
        IEnumerable<PlannedSettingChange>? changes = null,
        ExecutionPurpose purpose = ExecutionPurpose.Remediation,
        string revision = "revision-a",
        string emergencyRevision = "emergency-a",
        DateTimeOffset? expiresAt = null) => ChangePlanFactory.Create(
        Customer.OrganizationId,
        Customer.CustomerTenantId,
        purpose,
        revision,
        "requester-a",
        "baseline-1",
        "profile-1",
        "adapter-1",
        "evidence-fingerprint-1",
        emergencyRevision,
        "Exact setting impact.",
        "Manual recovery may be required.",
        expiresAt ?? Now.AddHours(1),
        changes ?? [Change()],
        Now);

    public static PlanApproval Approval(
        ChangePlanRevision? plan = null,
        string approverId = "approver-a",
        string? customerApproverId = "customer-approver-a",
        bool customerApprovalPresent = true,
        string authorizationVersion = "authz-1",
        DateTimeOffset? expiresAt = null) => new(
        Customer.OrganizationId,
        Customer.CustomerTenantId,
        (plan ?? Plan()).RevisionId,
        (plan ?? Plan()).ContentHash,
        (plan ?? Plan()).RequesterId,
        approverId,
        true,
        customerApprovalPresent,
        customerApproverId,
        authorizationVersion,
        Now.AddMinutes(-1),
        expiresAt ?? Now.AddMinutes(30));

    public static OperationPreflightEvidence Preflight(
        PlannedSettingChange? change = null,
        DateTimeOffset? collectedAt = null) => new(
        true,
        (change ?? Change()).BeforeValueJson,
        (change ?? Change()).ExpectedETag,
        true,
        true,
        true,
        "emergency-a",
        EmergencyPreflightDecision.Ready,
        true,
        true,
        false,
        true,
        collectedAt ?? Now);

    public static EmergencyRegistryRevision EmergencyRegistry(
        string revision = "emergency-a",
        EmergencyAccountRegistration? account = null) => new(
        Customer.OrganizationId,
        Customer.CustomerTenantId,
        revision,
        "customer-reviewer-a",
        Now.AddDays(-1),
        ImmutableArray.Create(account ?? EmergencyAccount()));

    public static EmergencyAccountRegistration EmergencyAccount(
        string objectId = "object-a",
        string displayName = "Emergency account") => new(
        objectId,
        Customer.CustomerTenantId,
        "owner-a",
        "recovery-admin",
        ImmutableArray.Create(new EmergencyAccountExclusion("ENT-001", "Recovery path", "owner-a")),
        Now.AddDays(-1),
        new RecoveryExerciseRecord(Now.AddDays(-1), "reviewer-a", "passed", "evidence-ref-a"),
        displayName);

    public static EmergencyAccountObservation EmergencyObservation(
        string objectId = "object-a",
        string tenantId = "tenant-a") => new(
        objectId,
        tenantId,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true);
}