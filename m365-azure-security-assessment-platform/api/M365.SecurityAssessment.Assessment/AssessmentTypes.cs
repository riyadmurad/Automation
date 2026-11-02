using System.Text.Json;

namespace M365.SecurityAssessment.Assessment;

public static class AssessmentContractVersions
{
    public const string BaselinePack = "1.0";
    public const string NormalizedEvidence = "1.0";
    public const string AdapterManifest = "1.0";
    public const string Evaluator = "1.0.0";
}

public enum NormalizedValueKind
{
    Boolean,
    String,
    Number,
    StringSet
}

public sealed record NormalizedValue(NormalizedValueKind Kind, JsonElement Value)
{
    public static NormalizedValue FromBoolean(bool value) =>
        new(NormalizedValueKind.Boolean, JsonSerializer.SerializeToElement(value));

    public static NormalizedValue FromString(string value) =>
        new(NormalizedValueKind.String, JsonSerializer.SerializeToElement(value));

    public static NormalizedValue FromNumber(decimal value) =>
        new(NormalizedValueKind.Number, JsonSerializer.SerializeToElement(value));

    public static NormalizedValue FromStringSet(IEnumerable<string> values) =>
        new(NormalizedValueKind.StringSet, JsonSerializer.SerializeToElement(values.Order(StringComparer.Ordinal).ToArray()));

    public void Validate(string name)
    {
        var valid = Kind switch
        {
            NormalizedValueKind.Boolean => Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            NormalizedValueKind.String => Value.ValueKind == JsonValueKind.String,
            NormalizedValueKind.Number => Value.ValueKind == JsonValueKind.Number,
            NormalizedValueKind.StringSet => Value.ValueKind == JsonValueKind.Array &&
                Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) &&
                Value.EnumerateArray().Select(item => item.GetString()!).Distinct(StringComparer.Ordinal).Count() == Value.GetArrayLength(),
            _ => false
        };

        if (!valid)
        {
            throw new AssessmentContractException($"Normalized value '{name}' does not match kind {Kind}.");
        }
    }
}

public static class NormalizedValueSemantics
{
    public static bool AreEquivalent(NormalizedValue left, NormalizedValue right)
    {
        left.Validate("left");
        right.Validate("right");
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            NormalizedValueKind.Boolean => left.Value.GetBoolean() == right.Value.GetBoolean(),
            NormalizedValueKind.String => left.Value.GetString() == right.Value.GetString(),
            NormalizedValueKind.Number => left.Value.GetDecimal() == right.Value.GetDecimal(),
            NormalizedValueKind.StringSet => left.Value.EnumerateArray().Select(item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal).SetEquals(right.Value.EnumerateArray().Select(item => item.GetString()!)),
            _ => false
        };
    }

    public static NormalizedValue Canonicalize(NormalizedValue value)
    {
        value.Validate("normalized value");
        return value.Kind == NormalizedValueKind.StringSet
            ? NormalizedValue.FromStringSet(value.Value.EnumerateArray().Select(item => item.GetString()!))
            : value;
    }
}

public enum AssessmentResultState
{
    Compliant,
    NonCompliant,
    NotApplicable,
    Exception,
    ManualReview,
    UnableToAssess
}

public enum CollectionOutcome
{
    Complete,
    Denied,
    Partial,
    Stale,
    Unsupported,
    UnsupportedCloud,
    MissingLicense,
    MissingPermission,
    Throttled,
    Error,
    Manual,
    NotApplicable,
    Exception
}

public enum SupportMode
{
    AutomatedVerified,
    Manual,
    Unsupported
}

public sealed record ExpectedInventory(
    Guid OrganizationId,
    Guid CustomerTenantId,
    string Name,
    string Source,
    bool Approved,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<string> ResourceIds);

public sealed record ApplicabilityEvidence(
    Guid OrganizationId,
    Guid CustomerTenantId,
    string ControlId,
    string EvidenceReference,
    string Source,
    Guid VerifiedBy,
    DateTimeOffset VerifiedAtUtc);

public sealed record RiskExceptionEvidence(
    string ExceptionId,
    Guid OrganizationId,
    Guid CustomerTenantId,
    string ControlId,
    string ApprovalReference,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record NormalizedEvidence(
    string ContractVersion,
    string BaselinePackId,
    string BaselinePackVersion,
    string BaselinePackHash,
    Guid OrganizationId,
    Guid CustomerTenantId,
    string ControlId,
    string Service,
    string SourceUrl,
    string SourceRevision,
    string SourceHash,
    string AdapterId,
    string AdapterVersion,
    DateTimeOffset CollectedAtUtc,
    CollectionOutcome CollectionOutcome,
    IReadOnlyDictionary<string, NormalizedValue> CurrentState,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyDictionary<string, ExpectedInventory> ExpectedInventories,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CoveredInventoryResources,
    ApplicabilityEvidence? Applicability,
    RiskExceptionEvidence? Exception);

public enum RuleOperator
{
    EqualsLiteral,
    EqualsField,
    EqualsExpectedInventory,
    IsSubsetOfField,
    IsEmpty,
    IsNonEmpty,
    IsTrue,
    IsFalse,
    IsZero
}

public sealed record ControlRule(
    string Field,
    RuleOperator Operator,
    string? RelatedField = null,
    NormalizedValue? Literal = null,
    string? Description = null);

public sealed record AssessmentControlDefinition(
    string Id,
    string Service,
    string BaselinePackId,
    string BaselinePackVersion,
    string BaselinePackHash,
    string SourceUrl,
    string SourceRevision,
    string SourceHash,
    string SupportMode,
    bool SourceReviewed,
    bool RightsApproved,
    bool RequiresExpectedInventory,
    IReadOnlyList<string> RequiredInventories,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<ControlRule> Rules,
    TimeSpan MaximumEvidenceAge);

public sealed record AssessmentDelta(
    string Field,
    string Expected,
    NormalizedValue? Current,
    bool? Matches,
    string Reason);

public sealed record AssessmentControlResult(
    string ContractVersion,
    string EvaluatorVersion,
    string BaselinePackId,
    string BaselinePackVersion,
    string BaselinePackHash,
    Guid OrganizationId,
    Guid CustomerTenantId,
    string ControlId,
    string Service,
    AssessmentResultState State,
    string Reason,
    IReadOnlyDictionary<string, NormalizedValue> CurrentState,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<ControlRule> ExpectedState,
    IReadOnlyList<AssessmentDelta> Deltas,
    int ExpectedCount,
    int CoveredCount,
    string SupportMode,
    bool SourceReviewed,
    bool RightsApproved,
    string SourceUrl,
    string SourceRevision,
    string SourceHash,
    string AdapterId,
    string AdapterVersion,
    DateTimeOffset CollectedAtUtc);

public sealed class AssessmentContractException(string message) : Exception(message);