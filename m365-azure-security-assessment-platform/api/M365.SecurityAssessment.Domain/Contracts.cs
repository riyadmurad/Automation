using System.Text.Json;

namespace M365.SecurityAssessment.Domain;

public static class ContractVersions
{
    public const string ApiResponse = "1.0";
    public const string BaselineInventory = "1.0";
    public const string Evidence = "1.0";
    public const string AdapterManifest = "1.0";
    public const string SyntheticFixture = "1.0";
    public const string JobEnvelope = "1.0";
    public const string Operation = "1.0";
}

public static class ContractVersionValidator
{
    public static void ValidateIngress(object contract)
    {
        switch (contract)
        {
            case BaselineInventoryDocument inventory:
                ValidateVersion("baseline inventory", inventory.SchemaVersion, ContractVersions.BaselineInventory);
                break;
            case SyntheticFixtureDocument fixture:
                ValidateVersion("synthetic fixture", fixture.SchemaVersion, ContractVersions.SyntheticFixture);
                break;
            case EvidenceRecord evidence:
                ValidateVersion("evidence", evidence.ContractVersion, ContractVersions.Evidence);
                break;
            case AdapterManifest manifest:
                ValidateVersion("adapter manifest", manifest.ContractVersion, ContractVersions.AdapterManifest);
                break;
            case OperationContract operation:
                ValidateVersion("operation", operation.ContractVersion, ContractVersions.Operation);
                if (!Enum.IsDefined(operation.Mode))
                {
                    throw new ContractValidationException("Operation mode is not defined.");
                }
                break;
            case SyntheticAssessmentResponse response:
                ValidateApiResponse(response.ContractVersion);
                foreach (var control in response.Controls)
                {
                    ValidateEvaluationEnums(control);
                }
                break;
            case SyntheticControlEvaluation evaluation:
                ValidateEvaluationEnums(evaluation);
                break;
            case SyntheticFixtureSummary summary:
                ValidateApiResponse(summary.ContractVersion);
                break;
            case JobEnvelope envelope:
                ValidateVersion("job envelope", envelope.ContractVersion, ContractVersions.JobEnvelope);
                if (!Enum.IsDefined(envelope.Purpose))
                {
                    throw new ContractValidationException("Job purpose is not defined.");
                }
                break;
            case System.Collections.IEnumerable collection when contract is not string:
                foreach (var item in collection)
                {
                    if (item is null)
                    {
                        throw new ContractValidationException("Contract collections cannot contain null entries.");
                    }
                    ValidateIngress(item);
                }
                break;
        }
    }

    public static void ValidateApiResponse(string version) =>
        ValidateVersion("API response", version, ContractVersions.ApiResponse);

    private static void ValidateVersion(string contractName, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new UnsupportedContractVersionException(contractName, actual);
        }
    }

    private static void ValidateEvaluationEnums(SyntheticControlEvaluation evaluation)
    {
        if (!Enum.IsDefined(evaluation.Status) || !Enum.IsDefined(evaluation.Reason))
        {
            throw new ContractValidationException("Assessment status or evaluation reason is not defined.");
        }
    }
}

public static class InitialControlIds
{
    public static readonly string[] All =
    [
        "ENT-001", "EXO-001", "TEAM-001", "SPO-001", "PP-001", "INT-001", "MDI-001",
        "MDO-001", "MDE-001", "MDCA-001", "XDR-001", "PUR-001", "AZR-001"
    ];
}

public sealed record BaselineInventoryDocument
{
    public string SchemaVersion { get; init; } = "";
    public IReadOnlyList<ControlDefinition> Controls { get; init; } = [];
}

public sealed record ControlDefinition
{
    public string Id { get; init; } = "";
    public string Service { get; init; } = "";
    public string SourceOrganization { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string ReviewStatus { get; init; } = "";
    public string SupportMode { get; init; } = "";
    public string[] NormalizedFields { get; init; } = [];
    public string[] SetFields { get; init; } = [];
    public string? ResourceIdField { get; init; }
    public Dictionary<string, string> SetEqualityFields { get; init; } = [];
    public Dictionary<string, string> SetSubsetFields { get; init; } = [];
    public string[] EmptySetFields { get; init; } = [];
    public string[] NonEmptySetFields { get; init; } = [];
    public Dictionary<string, JsonElement> ExpectedState { get; init; } = [];
    public string[] PermissionPrerequisites { get; init; } = [];
    public string[] LicensePrerequisites { get; init; } = [];
    public string[] RuntimePrerequisites { get; init; } = [];
    public string? SourceRevision { get; init; }
    public string? ContentHash { get; init; }
}

public sealed record SyntheticFixtureDocument
{
    public string SchemaVersion { get; init; } = "";
    public string Source { get; init; } = "";
    public bool LiveConnections { get; init; }
    public bool WritesEnabled { get; init; }
    public Guid TenantId { get; init; }
    public string CustomerName { get; init; } = "";
    public DateTimeOffset CollectedAtUtc { get; init; }
    public IReadOnlyList<SyntheticControlFixture> Controls { get; init; } = [];
}

public sealed record SyntheticControlFixture
{
    public string ControlId { get; init; } = "";
    public CollectionState CollectionState { get; init; }
    public string[] ResourceIds { get; init; } = [];
    public Dictionary<string, JsonElement> ObservedState { get; init; } = [];
}

public enum CollectionState { Complete, Denied, Partial, Stale, Unsupported, MissingLicense, Manual }
public enum AssessmentStatus { Compliant, NonCompliant, NotApplicable, Exception, ManualReview, UnableToAssess }
public enum EvaluationReason
{
    ExpectedStateObserved, ExpectedStateMismatch, CollectionDenied, CollectionPartial, EvidenceStale,
    ServiceUnsupported, LicenseUnavailable, ManualEvidenceRequired, RequiredEvidenceMissing
}

public sealed record SyntheticControlEvaluation(
    string ControlId,
    AssessmentStatus Status,
    EvaluationReason Reason,
    string SourceUrl,
    string ReviewStatus,
    string SupportMode,
    string? SourceRevision,
    IReadOnlyDictionary<string, JsonElement> ExpectedState,
    IReadOnlyDictionary<string, JsonElement> ObservedState,
    IReadOnlyList<string> ResourceIds,
    DateTimeOffset CollectedAtUtc,
    IReadOnlyList<string> EvidenceReferences,
    bool? SimulationMatchesExpected);

public sealed record SyntheticAssessmentResponse(
    string ContractVersion,
    string Source,
    bool LiveConnections,
    bool WritesEnabled,
    string EvaluationMode,
    Guid TenantId,
    string CustomerName,
    DateTimeOffset CollectedAtUtc,
    IReadOnlyList<SyntheticControlEvaluation> Controls);

public sealed record SyntheticFixtureSummary(
    string ContractVersion, string Source, bool LiveConnections, bool WritesEnabled,
    Guid TenantId, string CustomerName, int ControlCount);

public sealed record EvidenceRecord(
    string ContractVersion,
    Guid TenantId,
    string Service,
    string ResourceId,
    DateTimeOffset CollectedAtUtc,
    string Source,
    string SupportMode,
    string AdapterVersion,
    string IntegrityReference);

public sealed record AdapterManifest(
    string ContractVersion,
    string AdapterId,
    string Service,
    string SupportMode,
    IReadOnlyList<string> ReadOperations,
    IReadOnlyList<string> WriteOperations,
    string Runtime);

public enum OperationMode { GuidanceOnly, Disabled, Enabled }

public sealed record OperationContract(
    string ContractVersion,
    string OperationId,
    string Service,
    string Setting,
    OperationMode Mode,
    string Limitation);

public class ContractValidationException(string message) : Exception(message);

public sealed class UnsupportedContractVersionException(string contractName, string version)
    : ContractValidationException($"Unsupported {contractName} version '{version}'.");