namespace M365.SecurityAssessment.Domain;

public enum AuditValueKind { Known, Unknown, Absent, Redacted }
public enum AuditValueSensitivity { ClassifiedUnknown, NonSensitive, Sensitive }

public sealed record AuditValue(AuditValueKind Kind, string? Value, AuditValueSensitivity Sensitivity)
{
    public static AuditValue Known(string value, AuditValueSensitivity sensitivity) =>
        new(AuditValueKind.Known, value, sensitivity);
    public static AuditValue Unknown() => new(AuditValueKind.Unknown, null, AuditValueSensitivity.ClassifiedUnknown);
    public static AuditValue Absent() => new(AuditValueKind.Absent, null, AuditValueSensitivity.ClassifiedUnknown);
    public static AuditValue Redacted() => new(AuditValueKind.Redacted, "[REDACTED]", AuditValueSensitivity.Sensitive);
}

public sealed class SettingAuditEvent
{
    public SettingAuditEvent(DateTimeOffset occurredAtUtc, Guid organizationId, Guid customerTenantId, Guid actorId,
        string serviceName, string resourceId, string settingName, AuditValue previousValue,
        AuditValue requestedNewValue, AuditValue verifiedValue, string outcome, Guid requestId)
    {
        OccurredAtUtc = occurredAtUtc;
        OrganizationId = organizationId;
        CustomerTenantId = customerTenantId;
        ActorId = actorId;
        ServiceName = serviceName;
        ResourceId = resourceId;
        SettingName = settingName;
        PreviousValue = previousValue;
        RequestedNewValue = requestedNewValue;
        VerifiedValue = verifiedValue;
        Outcome = outcome;
        RequestId = requestId;
    }

    public DateTimeOffset OccurredAtUtc { get; }
    public Guid OrganizationId { get; }
    public Guid CustomerTenantId { get; }
    public Guid ActorId { get; }
    public string ServiceName { get; }
    public string ResourceId { get; }
    public string SettingName { get; }
    public AuditValue PreviousValue { get; }
    public AuditValue RequestedNewValue { get; }
    public AuditValue VerifiedValue { get; }
    public string Outcome { get; }
    public Guid RequestId { get; }
}

public static class SettingAuditEventValidator
{
    public static void Validate(SettingAuditEvent auditEvent)
    {
        if (auditEvent.OccurredAtUtc.Offset != TimeSpan.Zero || auditEvent.OrganizationId == Guid.Empty ||
            auditEvent.CustomerTenantId == Guid.Empty || auditEvent.ActorId == Guid.Empty || auditEvent.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(auditEvent.ServiceName) || string.IsNullOrWhiteSpace(auditEvent.ResourceId) ||
            string.IsNullOrWhiteSpace(auditEvent.SettingName) || string.IsNullOrWhiteSpace(auditEvent.Outcome))
        {
            throw new ContractValidationException("Audit events require UTC time, actor identity, setting scope, outcome, and correlation IDs.");
        }

        ValidateValue(auditEvent.SettingName, auditEvent.PreviousValue);
        ValidateValue(auditEvent.SettingName, auditEvent.RequestedNewValue);
        ValidateValue(auditEvent.SettingName, auditEvent.VerifiedValue);
    }

    private static void ValidateValue(string settingName, AuditValue value)
    {
        if (!Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.Sensitivity))
        {
            throw new ContractValidationException("Audit value kind or sensitivity is not defined.");
        }

        var valid = value.Kind switch
        {
            AuditValueKind.Known => value.Value is not null,
            AuditValueKind.Redacted => value.Value == "[REDACTED]",
            _ => value.Value is null
        };

        if (!valid)
        {
            throw new ContractValidationException("Audit values must preserve known, unknown, absent, or redacted state explicitly.");
        }

        if (value.Kind == AuditValueKind.Known &&
            AuditValueRedactor.Redact(settingName, value).Kind != AuditValueKind.Known)
        {
            throw new ContractValidationException("Sensitive or unclassified audit values must be redacted before persistence.");
        }
    }
}

public static class AuditValueRedactor
{
    private static readonly string[] SensitiveNameFragments =
        ["password", "secret", "token", "privatekey", "recoverycode", "credential", "apikey", "accesskey"];

    public static AuditValue Redact(string settingName, AuditValue value)
    {
        if (value.Kind != AuditValueKind.Known)
        {
            return value;
        }

        var normalizedName = new string(settingName.Where(char.IsLetterOrDigit).ToArray());
        if (value.Sensitivity != AuditValueSensitivity.NonSensitive ||
            SensitiveNameFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            LooksLikeSecret(value.Value!))
        {
            return AuditValue.Redacted();
        }

        return value;
    }

    private static bool LooksLikeSecret(string value)
    {
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) ||
            value.Contains("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal) ||
            value.Contains("-----BEGIN EC PRIVATE KEY-----", StringComparison.Ordinal))
        {
            return true;
        }

        var tokenParts = value.Split('.');
        if (tokenParts.Length == 3 && tokenParts.All(part => part.Length >= 8 &&
            part.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')))
        {
            return true;
        }

        return value.Length >= 40 && !value.Any(char.IsWhiteSpace) &&
            value.Any(char.IsLower) && value.Any(char.IsUpper) && value.Any(char.IsDigit);
    }
}