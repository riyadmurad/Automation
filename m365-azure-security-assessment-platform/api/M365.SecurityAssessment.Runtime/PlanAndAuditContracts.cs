using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace M365.SecurityAssessment.Runtime;

public sealed record PlannedSettingChange(
    string OperationId,
    string TargetOrganizationId,
    string TargetTenantId,
    string Service,
    string ResourceId,
    string SettingPath,
    string BeforeValueJson,
    string RequestedValueJson,
    string ExpectedETag,
    bool LockoutSensitive);

public sealed record ChangePlanRevision(
    string OrganizationId,
    string CustomerTenantId,
    ExecutionPurpose Purpose,
    string RevisionId,
    string RequesterId,
    string BaselineVersion,
    string ProfileVersion,
    string AdapterVersion,
    string EvidenceFingerprint,
    string EmergencyRegistryRevision,
    string ImpactDescription,
    string RecoveryLimitations,
    DateTimeOffset ExpiresAtUtc,
    ImmutableArray<PlannedSettingChange> Changes,
    string ContentHash);

public static class ChangePlanFactory
{
    public static ChangePlanRevision Create(
        string organizationId,
        string customerTenantId,
        ExecutionPurpose purpose,
        string revisionId,
        string requesterId,
        string baselineVersion,
        string profileVersion,
        string adapterVersion,
        string evidenceFingerprint,
        string emergencyRegistryRevision,
        string impactDescription,
        string recoveryLimitations,
        DateTimeOffset expiresAtUtc,
        IEnumerable<PlannedSettingChange> changes,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!Enum.IsDefined(purpose) || expiresAtUtc.Offset != TimeSpan.Zero ||
            nowUtc.Offset != TimeSpan.Zero || expiresAtUtc <= nowUtc)
        {
            throw new ArgumentException("Plan expiry and current time must be UTC, with expiry in the future.");
        }

        var immutableChanges = changes.OrderBy(change => change.OperationId, StringComparer.Ordinal).ToImmutableArray();
        if (immutableChanges.IsDefaultOrEmpty || immutableChanges.Any(change =>
                string.IsNullOrWhiteSpace(change.OperationId) ||
                !string.Equals(change.TargetOrganizationId, organizationId, StringComparison.Ordinal) ||
                !string.Equals(change.TargetTenantId, customerTenantId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(change.Service) ||
                string.IsNullOrWhiteSpace(change.ResourceId) ||
                string.IsNullOrWhiteSpace(change.SettingPath) ||
                string.IsNullOrWhiteSpace(change.ExpectedETag) ||
                !IsJson(change.BeforeValueJson) ||
                !IsJson(change.RequestedValueJson)))
        {
            throw new ArgumentException("A plan requires fully scoped setting changes and expected ETags.");
        }

        if (immutableChanges.Select(change => change.OperationId).Distinct(StringComparer.Ordinal).Count() != immutableChanges.Length)
        {
            throw new ArgumentException("Operation IDs must be unique within a plan.");
        }

        var required = new[]
        {
            organizationId, customerTenantId, revisionId, requesterId, baselineVersion,
            profileVersion, adapterVersion, evidenceFingerprint, emergencyRegistryRevision,
            impactDescription, recoveryLimitations
        };
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Plan ownership and provenance fields are required.");
        }

        var hash = ComputeContentHash(
            organizationId,
            customerTenantId,
            purpose,
            revisionId,
            requesterId,
            baselineVersion,
            profileVersion,
            adapterVersion,
            evidenceFingerprint,
            emergencyRegistryRevision,
            impactDescription,
            recoveryLimitations,
            expiresAtUtc,
            immutableChanges);

        return new ChangePlanRevision(
            organizationId,
            customerTenantId,
            purpose,
            revisionId,
            requesterId,
            baselineVersion,
            profileVersion,
            adapterVersion,
            evidenceFingerprint,
            emergencyRegistryRevision,
            impactDescription,
            recoveryLimitations,
            expiresAtUtc,
            immutableChanges,
            hash);
    }

    public static string RecomputeContentHash(ChangePlanRevision plan) => ComputeContentHash(
        plan.OrganizationId,
        plan.CustomerTenantId,
        plan.Purpose,
        plan.RevisionId,
        plan.RequesterId,
        plan.BaselineVersion,
        plan.ProfileVersion,
        plan.AdapterVersion,
        plan.EvidenceFingerprint,
        plan.EmergencyRegistryRevision,
        plan.ImpactDescription,
        plan.RecoveryLimitations,
        plan.ExpiresAtUtc,
        plan.Changes);

    private static string ComputeContentHash(
        string organizationId,
        string customerTenantId,
        ExecutionPurpose purpose,
        string revisionId,
        string requesterId,
        string baselineVersion,
        string profileVersion,
        string adapterVersion,
        string evidenceFingerprint,
        string emergencyRegistryRevision,
        string impactDescription,
        string recoveryLimitations,
        DateTimeOffset expiresAtUtc,
        ImmutableArray<PlannedSettingChange> changes)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, organizationId);
            Write(writer, customerTenantId);
            writer.Write((int)purpose);
            Write(writer, revisionId);
            Write(writer, requesterId);
            Write(writer, baselineVersion);
            Write(writer, profileVersion);
            Write(writer, adapterVersion);
            Write(writer, evidenceFingerprint);
            Write(writer, emergencyRegistryRevision);
            Write(writer, impactDescription);
            Write(writer, recoveryLimitations);
            writer.Write(expiresAtUtc.UtcTicks);
            writer.Write(changes.Length);
            foreach (var change in changes.OrderBy(change => change.OperationId, StringComparer.Ordinal))
            {
                Write(writer, change.OperationId);
                Write(writer, change.TargetOrganizationId);
                Write(writer, change.TargetTenantId);
                Write(writer, change.Service);
                Write(writer, change.ResourceId);
                Write(writer, change.SettingPath);
                Write(writer, change.BeforeValueJson);
                Write(writer, change.RequestedValueJson);
                Write(writer, change.ExpectedETag);
                writer.Write(change.LockoutSensitive);
            }
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void Write(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static bool IsJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record ApprovalPolicy(bool RequireCustomerApproval);

public sealed record PlanApproval(
    string OrganizationId,
    string CustomerTenantId,
    string PlanRevisionId,
    string PlanContentHash,
    string RequesterId,
    string ApproverId,
    bool ApproverCurrentlyAuthorized,
    bool CustomerApprovalPresent,
    string? CustomerApproverId,
    string AuthorizationVersion,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public enum ApprovalDecision
{
    Approved,
    Missing,
    WrongTenant,
    WrongPlan,
    Expired,
    SelfApproval,
    ApproverUnauthorized,
    CustomerApprovalRequired,
    AuthorizationChanged
}

public static class PlanApprovalPolicy
{
    public static ApprovalDecision Evaluate(
        ChangePlanRevision plan,
        PlanApproval? approval,
        ApprovalPolicy policy,
        string currentAuthorizationVersion,
        DateTimeOffset nowUtc)
    {
        if (approval is null)
        {
            return ApprovalDecision.Missing;
        }

        if (!string.Equals(plan.OrganizationId, approval.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(plan.CustomerTenantId, approval.CustomerTenantId, StringComparison.Ordinal))
        {
            return ApprovalDecision.WrongTenant;
        }

        if (!string.Equals(plan.RequesterId, approval.RequesterId, StringComparison.Ordinal))
        {
            return ApprovalDecision.WrongPlan;
        }

        if (!string.Equals(plan.RevisionId, approval.PlanRevisionId, StringComparison.Ordinal) ||
            !string.Equals(plan.ContentHash, approval.PlanContentHash, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(plan.ContentHash),
                Encoding.ASCII.GetBytes(ChangePlanFactory.RecomputeContentHash(plan))))
        {
            return ApprovalDecision.WrongPlan;
        }

        if (nowUtc.Offset != TimeSpan.Zero || approval.ExpiresAtUtc <= nowUtc ||
            plan.ExpiresAtUtc <= nowUtc || approval.ExpiresAtUtc <= approval.ApprovedAtUtc)
        {
            return ApprovalDecision.Expired;
        }

        if (string.Equals(plan.RequesterId, approval.ApproverId, StringComparison.Ordinal))
        {
            return ApprovalDecision.SelfApproval;
        }

        if (!approval.ApproverCurrentlyAuthorized)
        {
            return ApprovalDecision.ApproverUnauthorized;
        }

        if (policy.RequireCustomerApproval &&
            (!approval.CustomerApprovalPresent ||
             string.IsNullOrWhiteSpace(approval.CustomerApproverId) ||
             string.Equals(plan.RequesterId, approval.CustomerApproverId, StringComparison.Ordinal)))
        {
            return ApprovalDecision.CustomerApprovalRequired;
        }

        if (approval.ApprovedAtUtc.Offset != TimeSpan.Zero || approval.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            approval.ApprovedAtUtc > nowUtc)
        {
            return ApprovalDecision.Expired;
        }

        return string.Equals(approval.AuthorizationVersion, currentAuthorizationVersion, StringComparison.Ordinal)
            ? ApprovalDecision.Approved
            : ApprovalDecision.AuthorizationChanged;
    }
}

public enum AuditValueState
{
    Known,
    Unknown,
    Absent,
    Redacted
}

public sealed record AuditValue(AuditValueState State, string? JsonValue)
{
    public static AuditValue Known(string jsonValue) => new(AuditValueState.Known, jsonValue);
    public static AuditValue Unknown() => new(AuditValueState.Unknown, null);
    public static AuditValue Absent() => new(AuditValueState.Absent, null);
    public static AuditValue Redacted() => new(AuditValueState.Redacted, null);

    public bool IsValid
    {
        get
        {
            if (State != AuditValueState.Known)
            {
                return JsonValue is null;
            }

            if (JsonValue is null)
            {
                return false;
            }

            try
            {
                using var _ = JsonDocument.Parse(JsonValue);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}

public sealed record SettingAuditIntent(
    string EventId,
    DateTimeOffset TimestampUtc,
    string OrganizationId,
    string CustomerTenantId,
    string AuthorizedUserId,
    string RequesterId,
    string ApproverId,
    string ExecutingWorkloadId,
    string Service,
    string ResourceId,
    string SettingPath,
    AuditValue PreviousValue,
    AuditValue RequestedValue,
    string PlanRevisionId,
    string OperationId,
    string CorrelationId);

public sealed record SettingAuditResult(
    string EventId,
    DateTimeOffset TimestampUtc,
    string PlanRevisionId,
    string OperationId,
    string Outcome,
    AuditValue VerifiedValue,
    string? ErrorCategory,
    string CorrelationId);

public static class SettingAuditContract
{
    public static bool IsValidIntent(SettingAuditIntent intent) =>
        intent.TimestampUtc.Offset == TimeSpan.Zero &&
        new[]
        {
            intent.EventId, intent.OrganizationId, intent.CustomerTenantId, intent.AuthorizedUserId,
            intent.RequesterId, intent.ApproverId, intent.ExecutingWorkloadId, intent.Service,
            intent.ResourceId, intent.SettingPath, intent.PlanRevisionId, intent.OperationId, intent.CorrelationId
        }.All(value => !string.IsNullOrWhiteSpace(value)) &&
        intent.PreviousValue.IsValid &&
        intent.RequestedValue.IsValid;

    public static bool IsValidResult(SettingAuditResult result) =>
        result.TimestampUtc.Offset == TimeSpan.Zero &&
        new[] { result.EventId, result.PlanRevisionId, result.OperationId, result.Outcome, result.CorrelationId }
            .All(value => !string.IsNullOrWhiteSpace(value)) &&
        result.VerifiedValue.IsValid;

    public static AuditValue RedactIfSensitive(string settingPath, AuditValue value)
    {
        var path = settingPath.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var sensitive = new[] { "password", "token", "secret", "privatekey", "recoverycode", "certificatekey" };
        return sensitive.Any(path.Contains) ? AuditValue.Redacted() : value;
    }
}