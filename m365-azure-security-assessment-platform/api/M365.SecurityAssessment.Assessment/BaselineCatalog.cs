using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace M365.SecurityAssessment.Assessment;

public enum BaselineSourceClassification
{
    MicrosoftGuidance,
    CisaScuba,
    CisBenchmark,
    SansPolicy
}

public enum SourceRightsStatus
{
    CitationOnly,
    LicensedForRedistribution,
    RightsUnverified,
    RedistributionProhibited
}

public enum ReviewState
{
    PendingReview,
    Approved
}

public enum ExpectedStateClassification
{
    SourceRecommendationCandidate,
    CustomerPolicyCandidate,
    Unresolved
}

public enum ProfileLayerKind
{
    Platform,
    Msp,
    Customer
}

public sealed record BaselinePackDocument(
    string SchemaVersion,
    string PackId,
    string Version,
    ReviewState ReviewState,
    bool ContainsExecutableContent,
    bool ContainsSourceText,
    IReadOnlyList<BaselineSourceSnapshot> Sources,
    IReadOnlyList<BaselineControl> Controls);

public sealed record BaselineSourceSnapshot(
    string Id,
    string SourceOrganization,
    BaselineSourceClassification Classification,
    string RequestedUrl,
    string ResolvedUrl,
    int HttpStatus,
    string RetrievalStatus,
    DateTimeOffset RetrievedAtUtc,
    string ContentType,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    long ResponseByteLength,
    string ResponseSha256,
    string SourceRevision,
    SourceRightsStatus RightsStatus,
    bool ContentIncluded,
    ReviewState ReviewState,
    string ReviewNote);

public sealed record SourceRetrievalFailure(
    string ControlId,
    string RequestedUrl,
    int HttpStatus,
    DateTimeOffset RetrievedAtUtc,
    long ResponseByteLength,
    string ResponseSha256,
    string Disposition);

public sealed record BaselineControl(
    string Id,
    string Service,
    string SpecificationScenario,
    string ImplementationTask,
    string FixtureFamily,
    string SourceId,
    ExpectedStateClassification ExpectedStateClassification,
    ReviewState ReviewState,
    bool RightsApproved,
    string SupportMode,
    bool OperationQualified,
    bool RequiresExpectedInventory,
    IReadOnlyList<string> RequiredInventories,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<ControlRule> Rules,
    IReadOnlyList<string> PermissionPrerequisites,
    IReadOnlyList<string> LicensePrerequisites,
    IReadOnlyList<string> RuntimePrerequisites,
    string ExpectedStateReviewNote,
    int? MaximumEvidenceAgeHours);

public sealed record BaselineApproval(
    string ApprovalId,
    string PackHash,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string Scope,
    string Purpose);

public sealed record BaselinePackRelease(
    string PackId,
    string Version,
    string ContentHash,
    BaselinePackDocument Pack);

public sealed record SourceUpdateNotice(
    string SourceId,
    string SourceUrl,
    string PreviousRevision,
    string ObservedRevision,
    DateTimeOffset DetectedAtUtc,
    string State,
    IReadOnlyList<string> ChangedMetadata,
    bool ContentReviewRequired,
    bool MayActivate);

public sealed record BaselineProfileLayer(
    string LayerId,
    ProfileLayerKind Kind,
    Guid? OrganizationId,
    Guid? CustomerTenantId,
    string PackHash,
    IReadOnlyDictionary<string, NormalizedValue> Values,
    string ApprovalReference,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

public sealed record ProfileConflictChoice(
    string SettingPath,
    string SelectedLayerId,
    string ApprovalReference,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

public sealed record EffectiveProfileSetting(
    string SettingPath,
    NormalizedValue Value,
    string OriginLayerId,
    IReadOnlyList<string> InheritedFromLayerIds,
    string? ConflictApprovalReference);

public sealed record EffectiveProfileRelease(
    string ProfileId,
    string Version,
    Guid OrganizationId,
    Guid CustomerTenantId,
    string ContentHash,
    IReadOnlyDictionary<string, EffectiveProfileSetting> Settings,
    IReadOnlyList<string> PackHashes,
    IReadOnlyList<string> ConflictApprovalReferences);

public sealed record EffectiveProfileApproval(
    string ApprovalReference,
    string ProfileHash,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    Guid OrganizationId,
    Guid CustomerTenantId);

public sealed class BaselinePackJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static BaselinePackDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BaselinePackDocument>(json, SerializerOptions)
            ?? throw new AssessmentContractException("Baseline pack JSON was empty.");
    }

    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class BaselinePackValidator
{
    private static readonly string[] InitialControlIds =
    [
        "ENT-001", "EXO-001", "TEAM-001", "SPO-001", "PP-001", "INT-001", "MDI-001",
        "MDO-001", "MDE-001", "MDCA-001", "XDR-001", "PUR-001", "AZR-001"
    ];

    public static void Validate(BaselinePackDocument pack)
    {
        if (pack.SchemaVersion != AssessmentContractVersions.BaselinePack)
        {
            throw new AssessmentContractException($"Unsupported baseline pack schema '{pack.SchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(pack.PackId) || !IsSemanticVersion(pack.Version))
        {
            throw new AssessmentContractException("Baseline pack ID and semantic version are required.");
        }

        if (pack.ContainsExecutableContent || pack.ContainsSourceText)
        {
            throw new AssessmentContractException("Baseline packs cannot contain executable remediation or source text.");
        }

        if (pack.Controls.Count != InitialControlIds.Length ||
            !pack.Controls.Select(control => control.Id).SequenceEqual(InitialControlIds, StringComparer.Ordinal))
        {
            throw new AssessmentContractException("The initial qualified pack must contain each of the 13 stable control IDs exactly once and in canonical order.");
        }

        if (pack.Sources.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != pack.Sources.Count)
        {
            throw new AssessmentContractException("Source snapshot IDs must be unique within a pack.");
        }

        var sources = pack.Sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        foreach (var source in pack.Sources)
        {
            ValidateSource(source);
        }

        foreach (var control in pack.Controls)
        {
            if (!sources.ContainsKey(control.SourceId))
            {
                throw new AssessmentContractException($"Control {control.Id} references missing source snapshot {control.SourceId}.");
            }

            if (string.IsNullOrWhiteSpace(control.Service) || control.RequiredFields.Count == 0 ||
                control.Rules.Count == 0 || control.MaximumEvidenceAgeHours is <= 0 ||
                string.IsNullOrWhiteSpace(control.SpecificationScenario) ||
                string.IsNullOrWhiteSpace(control.ImplementationTask) || string.IsNullOrWhiteSpace(control.FixtureFamily))
            {
                throw new AssessmentContractException($"Control {control.Id} is missing required assessment metadata.");
            }

            if (control.SupportMode == "automated/verified" && !control.OperationQualified)
            {
                throw new AssessmentContractException($"Control {control.Id} cannot claim automation without an operation qualification record.");
            }
            if (control.SupportMode is not ("automated/verified" or "manual/unverified" or "unsupported") ||
                control.ExpectedStateClassification == ExpectedStateClassification.Unresolved && control.SupportMode != "manual/unverified")
            {
                throw new AssessmentContractException($"Control {control.Id} has an unsupported mode for its source-review state.");
            }

            if (control.RequiresExpectedInventory && control.RequiredInventories.Count == 0)
            {
                throw new AssessmentContractException($"Control {control.Id} requires at least one named expected inventory.");
            }

            var declaredFields = control.RequiredFields.ToHashSet(StringComparer.Ordinal);
            foreach (var rule in control.Rules)
            {
                var relatedFieldIsValid = rule.Operator switch
                {
                    RuleOperator.EqualsExpectedInventory => rule.RelatedField is not null &&
                        control.RequiredInventories.Contains(rule.RelatedField, StringComparer.Ordinal),
                    RuleOperator.EqualsField or RuleOperator.IsSubsetOfField => rule.RelatedField is not null &&
                        declaredFields.Contains(rule.RelatedField),
                    _ => rule.RelatedField is null
                };
                if (!declaredFields.Contains(rule.Field) || !relatedFieldIsValid)
                {
                    throw new AssessmentContractException($"Control {control.Id} has a rule referencing an undeclared field.");
                }

                if (rule.Operator == RuleOperator.EqualsLiteral)
                {
                    if (rule.Literal is null)
                    {
                        throw new AssessmentContractException($"Control {control.Id} literal rule '{rule.Field}' has no expected value.");
                    }
                    rule.Literal.Validate($"{control.Id}.{rule.Field}");
                }
                else if (rule.Literal is not null)
                {
                    throw new AssessmentContractException($"Control {control.Id} rule '{rule.Field}' has an unused literal.");
                }
            }
        }
    }

    public static void ValidateForActivation(BaselinePackDocument pack)
    {
        Validate(pack);
        if (pack.ReviewState != ReviewState.Approved ||
            pack.Sources.Any(source => source.ReviewState != ReviewState.Approved ||
                source.RightsStatus is SourceRightsStatus.RightsUnverified or SourceRightsStatus.RedistributionProhibited) ||
            pack.Controls.Any(control => control.ReviewState != ReviewState.Approved || !control.RightsApproved ||
                control.ExpectedStateClassification == ExpectedStateClassification.Unresolved))
        {
            throw new AssessmentContractException("Baseline activation requires reviewed sources, rights, expected states, and controls.");
        }
    }

    public static void ValidateRetrievedSource(BaselineSourceSnapshot source) => ValidateSource(source);

    private static void ValidateSource(BaselineSourceSnapshot source)
    {
        if (!IsHttps(source.RequestedUrl) || !IsHttps(source.ResolvedUrl) || source.HttpStatus is < 200 or >= 300 ||
            source.RetrievalStatus != "retrieved" || source.RetrievedAtUtc.Offset != TimeSpan.Zero ||
            source.ResponseByteLength <= 0 || !IsSha256(source.ResponseSha256) ||
            source.SourceRevision != $"sha256:{source.ResponseSha256}" || source.ContentIncluded)
        {
            throw new AssessmentContractException($"Source snapshot {source.Id} lacks a successful, pinned, content-free retrieval record.");
        }

        if (source.RightsStatus == SourceRightsStatus.LicensedForRedistribution && !source.ContentIncluded)
        {
            return;
        }

        if (source.ContentIncluded && source.RightsStatus != SourceRightsStatus.LicensedForRedistribution)
        {
            throw new AssessmentContractException($"Source snapshot {source.Id} contains content without approved reuse rights.");
        }
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();

    private static bool IsSemanticVersion(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]+\\.[0-9]+\\.[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

public static class CanonicalContentHash
{
    private static readonly JsonSerializerOptions SerializerOptions = BaselinePackJson.CreateSerializerOptions();

    public static string Compute(object value)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(value, SerializerOptions))
            ?? throw new AssessmentContractException("Unable to serialize content for hashing.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, node);
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}

public sealed class ImmutableBaselineCatalog
{
    private readonly object syncRoot = new();
    private readonly Dictionary<(string PackId, string Version), (string Hash, string Payload)> releases = [];
    private readonly Dictionary<(string PackId, string Version, string Hash), BaselineApproval> activeReleases = [];

    public BaselinePackRelease Publish(BaselinePackDocument pack)
    {
        BaselinePackValidator.Validate(pack);
        var options = BaselinePackJson.CreateSerializerOptions();
        var payload = JsonSerializer.Serialize(pack, options);
        var hash = CanonicalContentHash.Compute(pack);
        var key = (pack.PackId, pack.Version);
        lock (syncRoot)
        {
            if (releases.TryGetValue(key, out var existing) && existing.Hash != hash)
            {
                throw new AssessmentContractException("A published pack version is immutable; publish changed content as a new version.");
            }
            releases[key] = (hash, payload);
        }
        return CreateRelease(pack.PackId, pack.Version, hash, payload);
    }

    public BaselinePackRelease Get(string packId, string version)
    {
        (string Hash, string Payload) stored;
        lock (syncRoot)
        {
            if (!releases.TryGetValue((packId, version), out stored))
            {
                throw new KeyNotFoundException($"Baseline release {packId}@{version} was not found.");
            }
        }
        return CreateRelease(packId, version, stored.Hash, stored.Payload);
    }

    public BaselinePackRelease Activate(string packId, string version, BaselineApproval approval)
    {
        var release = Get(packId, version);
        BaselinePackValidator.ValidateForActivation(release.Pack);
        if (string.IsNullOrWhiteSpace(approval.ApprovalId) || approval.PackHash != release.ContentHash ||
            approval.ApprovedBy == Guid.Empty || approval.ApprovedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(approval.Scope) || approval.Purpose != "baseline-activation")
        {
            throw new AssessmentContractException("Baseline activation requires an explicit approval bound to the exact release hash and scope.");
        }

        var activationKey = (packId, version, release.ContentHash);
        lock (syncRoot)
        {
            if (activeReleases.TryGetValue(activationKey, out var existingApproval) && existingApproval != approval)
            {
                throw new AssessmentContractException("An activated pack release cannot have its approval silently replaced.");
            }
            activeReleases[activationKey] = approval;
        }
        return release;
    }

    public bool IsActive(string packId, string version, string contentHash)
    {
        lock (syncRoot)
        {
            return activeReleases.ContainsKey((packId, version, contentHash));
        }
    }

    public BaselineApproval GetActivationApproval(string packId, string version, string contentHash)
    {
        lock (syncRoot)
        {
            return activeReleases.TryGetValue((packId, version, contentHash), out var approval)
                ? approval
                : throw new KeyNotFoundException("No activation approval was recorded for this release.");
        }
    }

    private static BaselinePackRelease CreateRelease(string packId, string version, string hash, string payload)
    {
        var pack = JsonSerializer.Deserialize<BaselinePackDocument>(payload, BaselinePackJson.CreateSerializerOptions())
            ?? throw new AssessmentContractException("Stored baseline release could not be read.");
        return new BaselinePackRelease(packId, version, hash, pack);
    }
}

public static class BaselineProfileResolver
{
    public static EffectiveProfileRelease Resolve(
        string profileId,
        string version,
        Guid organizationId,
        Guid customerTenantId,
        IReadOnlyList<BaselineProfileLayer> layers,
        IReadOnlyList<ProfileConflictChoice> conflictChoices)
    {
        if (organizationId == Guid.Empty || customerTenantId == Guid.Empty || layers.Count == 0)
        {
            throw new AssessmentContractException("Profile resolution requires organization, customer tenant, and at least one layer.");
        }

        var ordered = layers.OrderBy(layer => layer.Kind).ToArray();
        if (ordered.Select(layer => layer.Kind).Distinct().Count() != ordered.Length ||
            ordered.Select(layer => layer.LayerId).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(layer => layer.Kind != ProfileLayerKind.Platform &&
                (layer.OrganizationId != organizationId || layer.ApprovedBy == Guid.Empty ||
                 string.IsNullOrWhiteSpace(layer.ApprovalReference) || layer.ApprovedAtUtc.Offset != TimeSpan.Zero ||
                 string.IsNullOrWhiteSpace(layer.PackHash))) ||
            ordered.Any(layer => layer.Kind == ProfileLayerKind.Msp && layer.CustomerTenantId is not null) ||
            ordered.Any(layer => layer.Kind == ProfileLayerKind.Customer && layer.CustomerTenantId != customerTenantId))
        {
            throw new AssessmentContractException("Profile layers must be unique and tenant-bound; MSP/customer overrides require approval.");
        }

        var choices = conflictChoices.ToDictionary(choice => choice.SettingPath, StringComparer.Ordinal);
        var allPaths = ordered.SelectMany(layer => layer.Values.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var resolved = new Dictionary<string, EffectiveProfileSetting>(StringComparer.Ordinal);
        var decisionReferences = new List<string>();
        var consumedChoices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in allPaths)
        {
            var candidates = ordered.Where(layer => layer.Values.ContainsKey(path)).ToArray();
            foreach (var candidate in candidates)
            {
                candidate.Values[path].Validate(path);
            }
            var distinctValues = new List<NormalizedValue>();
            foreach (var candidate in candidates)
            {
                if (!distinctValues.Any(value => NormalizedValueSemantics.AreEquivalent(value, candidate.Values[path])))
                {
                    distinctValues.Add(candidate.Values[path]);
                }
            }
            var selected = candidates[^1];
            string? conflictApproval = null;
            if (distinctValues.Count > 1)
            {
                if (!choices.TryGetValue(path, out var choice) || choice.ApprovedBy == Guid.Empty ||
                    string.IsNullOrWhiteSpace(choice.ApprovalReference) || choice.ApprovedAtUtc.Offset != TimeSpan.Zero)
                {
                    throw new AssessmentContractException($"Unresolved profile conflict for '{path}' blocks profile resolution.");
                }
                selected = candidates.SingleOrDefault(layer => layer.LayerId == choice.SelectedLayerId)
                    ?? throw new AssessmentContractException($"Conflict choice for '{path}' does not select a contributing layer.");
                conflictApproval = choice.ApprovalReference;
                decisionReferences.Add(choice.ApprovalReference);
                consumedChoices.Add(path);
            }

            resolved[path] = new EffectiveProfileSetting(
                path,
                NormalizedValueSemantics.Canonicalize(selected.Values[path]),
                selected.LayerId,
                candidates.Where(layer => layer.LayerId != selected.LayerId).Select(layer => layer.LayerId).ToArray(),
                conflictApproval);
        }

        if (choices.Keys.Any(path => !consumedChoices.Contains(path)))
        {
            throw new AssessmentContractException("Conflict approvals must correspond to an actual profile conflict.");
        }

        var provisional = new EffectiveProfileRelease(profileId, version, organizationId, customerTenantId, "",
            resolved, ordered.Select(layer => layer.PackHash).Distinct(StringComparer.Ordinal).ToArray(), decisionReferences);
        return provisional with { ContentHash = CanonicalContentHash.Compute(provisional) };
    }
}

public sealed class EffectiveProfileActivationCatalog
{
    private readonly object syncRoot = new();
    private readonly Dictionary<(Guid OrganizationId, Guid CustomerTenantId, string ProfileId, string Version), string> immutableVersions = [];
    private readonly Dictionary<(Guid OrganizationId, Guid CustomerTenantId, string ProfileId, string Version, string Hash), EffectiveProfileApproval> activations = [];

    public EffectiveProfileApproval Activate(
        EffectiveProfileRelease release,
        EffectiveProfileApproval approval)
    {
        if (approval.ApprovalReference.Length == 0 || approval.ProfileHash != release.ContentHash ||
            approval.ApprovedBy == Guid.Empty || approval.ApprovedAtUtc.Offset != TimeSpan.Zero ||
            approval.OrganizationId != release.OrganizationId || approval.CustomerTenantId != release.CustomerTenantId)
        {
            throw new AssessmentContractException("Profile activation requires explicit approval for the exact tenant-bound profile hash.");
        }

        var key = (release.OrganizationId, release.CustomerTenantId, release.ProfileId, release.Version, release.ContentHash);
        var versionKey = (release.OrganizationId, release.CustomerTenantId, release.ProfileId, release.Version);
        lock (syncRoot)
        {
            if (immutableVersions.TryGetValue(versionKey, out var existingHash) && existingHash != release.ContentHash)
            {
                throw new AssessmentContractException("An activated profile version is immutable; changed content needs a new version.");
            }
            if (activations.TryGetValue(key, out var existingApproval) && existingApproval != approval)
            {
                throw new AssessmentContractException("An activated profile release cannot have its approval silently replaced.");
            }
            immutableVersions[versionKey] = release.ContentHash;
            activations[key] = approval;
        }
        return approval;
    }

    public bool IsActive(EffectiveProfileRelease release)
    {
        var key = (release.OrganizationId, release.CustomerTenantId, release.ProfileId, release.Version, release.ContentHash);
        lock (syncRoot)
        {
            return activations.ContainsKey(key);
        }
    }

    public EffectiveProfileApproval GetApproval(EffectiveProfileRelease release)
    {
        var key = (release.OrganizationId, release.CustomerTenantId, release.ProfileId, release.Version, release.ContentHash);
        lock (syncRoot)
        {
            return activations.TryGetValue(key, out var approval)
                ? approval
                : throw new KeyNotFoundException("No activation approval was recorded for this profile release.");
        }
    }
}

public static class SourceUpdateDetector
{
    public static SourceUpdateNotice? Detect(
        BaselineSourceSnapshot pinned,
        BaselineSourceSnapshot observed,
        DateTimeOffset detectedAtUtc)
    {
        if (pinned.Id != observed.Id || detectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new AssessmentContractException("Source update comparisons require the same source ID and a UTC detection time.");
        }

        BaselinePackValidator.ValidateRetrievedSource(pinned);
        BaselinePackValidator.ValidateRetrievedSource(observed);

        if (pinned.SourceRevision == observed.SourceRevision)
        {
            return null;
        }

        var changedMetadata = new List<string> { "sourceRevision", "responseSha256" };
        if (pinned.ResolvedUrl != observed.ResolvedUrl) changedMetadata.Add("resolvedUrl");
        if (pinned.ETag != observed.ETag) changedMetadata.Add("etag");
        if (pinned.LastModifiedUtc != observed.LastModifiedUtc) changedMetadata.Add("lastModifiedUtc");
        if (pinned.ResponseByteLength != observed.ResponseByteLength) changedMetadata.Add("responseByteLength");
        return new SourceUpdateNotice(pinned.Id, observed.ResolvedUrl, pinned.SourceRevision, observed.SourceRevision,
            detectedAtUtc, "pending-review", changedMetadata, true, false);
    }
}

