using System.Globalization;
using System.Text.Json;

namespace M365.SecurityAssessment.Assessment;

public interface IAssessmentEvaluator
{
    AssessmentControlResult Evaluate(
        BaselinePackDocument pack,
        NormalizedEvidence evidence,
        DateTimeOffset nowUtc);
}

public sealed class AssessmentEvaluator : IAssessmentEvaluator
{
    public AssessmentControlResult Evaluate(
        BaselinePackDocument pack,
        NormalizedEvidence evidence,
        DateTimeOffset nowUtc)
    {
        BaselinePackValidator.Validate(pack);
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new AssessmentContractException("Evaluation time must be UTC.");
        }

        var control = pack.Controls.SingleOrDefault(item => item.Id == evidence.ControlId)
            ?? throw new AssessmentContractException($"Control '{evidence.ControlId}' is not present in the pinned baseline pack.");
        var source = pack.Sources.Single(item => item.Id == control.SourceId);
        var baselinePackHash = CanonicalContentHash.Compute(pack);
        var definition = new AssessmentControlDefinition(
            control.Id,
            control.Service,
            pack.PackId,
            pack.Version,
            baselinePackHash,
            source.ResolvedUrl,
            source.SourceRevision,
            source.ResponseSha256,
            control.SupportMode,
            control.ReviewState == ReviewState.Approved && source.ReviewState == ReviewState.Approved,
            control.RightsApproved && source.RightsStatus is SourceRightsStatus.CitationOnly or SourceRightsStatus.LicensedForRedistribution,
            control.RequiresExpectedInventory,
            control.RequiredInventories,
            control.RequiredFields,
            control.Rules,
            control.MaximumEvidenceAgeHours is { } maximumAgeHours
                ? TimeSpan.FromHours(maximumAgeHours)
                : TimeSpan.Zero);

        ValidateEvidenceIdentity(definition, evidence);
        var expectedCount = definition.RequiredInventories.Count == 0 ? 1 :
            evidence.ExpectedInventories.TryGetValue(definition.RequiredInventories[0], out var primaryInventory)
                ? primaryInventory.ResourceIds.Count
                : 0;
        var coveredCount = definition.RequiredInventories.Count == 0 ? 0 :
            evidence.CoveredInventoryResources.TryGetValue(definition.RequiredInventories[0], out var primaryCoverage)
                ? primaryCoverage.Distinct(StringComparer.Ordinal).Count()
                : 0;

        var earlyResult = EvaluateCollectionState(definition, evidence, expectedCount, coveredCount, nowUtc);
        if (earlyResult is not null)
        {
            return earlyResult;
        }

        if (evidence.EvidenceReferences.Count == 0 || evidence.EvidenceReferences.Any(string.IsNullOrWhiteSpace) ||
            evidence.EvidenceReferences.Distinct(StringComparer.Ordinal).Count() != evidence.EvidenceReferences.Count)
        {
            return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                "Complete evidence is missing unique, traceable evidence references.", [], expectedCount, coveredCount);
        }

        if (evidence.CurrentState.Keys.Except(definition.RequiredFields, StringComparer.Ordinal).Any())
        {
            throw new AssessmentContractException("Normalized evidence contains fields not declared by the pinned control schema.");
        }

        if (definition.RequiresExpectedInventory)
        {
            var inventoryFailure = ValidateInventories(definition, evidence, expectedCount, coveredCount);
            if (inventoryFailure is not null)
            {
                return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                    inventoryFailure, [], expectedCount, coveredCount);
            }
        }

        var missingField = definition.RequiredFields.FirstOrDefault(field => !evidence.CurrentState.ContainsKey(field));
        if (missingField is not null)
        {
            return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                $"Required normalized evidence '{missingField}' is absent.", [], expectedCount, coveredCount);
        }

        foreach (var pair in evidence.CurrentState)
        {
            pair.Value.Validate(pair.Key);
        }

        var deltas = definition.Rules.Select(rule => EvaluateRule(rule, evidence)).ToArray();
        if (deltas.Any(delta => delta.Matches is null))
        {
            return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                "A required rule operand or expected inventory is unavailable.", deltas, expectedCount, coveredCount);
        }

        var conforms = deltas.All(delta => delta.Matches == true);
        if (definition.SupportMode == "unsupported")
        {
            return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                "The control is explicitly unsupported.", deltas, expectedCount, coveredCount);
        }

        if (definition.SupportMode != "automated/verified" || !definition.SourceReviewed || !definition.RightsApproved)
        {
            return CreateResult(definition, evidence, AssessmentResultState.ManualReview,
                conforms
                    ? "Observed values match the candidate state, but source, rights, or operation review is incomplete."
                    : "Observed values differ from the candidate state; manual source and policy review is required.",
                deltas, expectedCount, coveredCount);
        }

        return CreateResult(definition, evidence,
            conforms ? AssessmentResultState.Compliant : AssessmentResultState.NonCompliant,
            conforms ? "All qualified expectations are observed." : "One or more qualified expectations are not observed.",
            deltas, expectedCount, coveredCount);
    }

    private static AssessmentControlResult? EvaluateCollectionState(
        AssessmentControlDefinition definition,
        NormalizedEvidence evidence,
        int expectedCount,
        int coveredCount,
        DateTimeOffset nowUtc)
    {
        if (evidence.CollectionOutcome == CollectionOutcome.NotApplicable)
        {
            var applicability = evidence.Applicability;
            var isVerified = applicability is not null &&
                applicability.OrganizationId == evidence.OrganizationId &&
                applicability.CustomerTenantId == evidence.CustomerTenantId &&
                applicability.ControlId == evidence.ControlId &&
                !string.IsNullOrWhiteSpace(applicability.EvidenceReference) &&
                !string.IsNullOrWhiteSpace(applicability.Source) &&
                applicability.VerifiedBy != Guid.Empty && applicability.VerifiedAtUtc.Offset == TimeSpan.Zero &&
                applicability.VerifiedAtUtc <= nowUtc;
            return CreateResult(definition, evidence,
                isVerified ? AssessmentResultState.NotApplicable : AssessmentResultState.UnableToAssess,
                isVerified ? $"Not applicable per verified evidence {applicability!.EvidenceReference}." :
                    "Not-applicable status has no tenant-bound, verified evidence.",
                [], expectedCount, coveredCount);
        }

        if (evidence.CollectionOutcome == CollectionOutcome.Exception)
        {
            var exception = evidence.Exception;
            var isApproved = exception is not null &&
                exception.OrganizationId == evidence.OrganizationId &&
                exception.CustomerTenantId == evidence.CustomerTenantId &&
                exception.ControlId == evidence.ControlId &&
                !string.IsNullOrWhiteSpace(exception.ExceptionId) &&
                !string.IsNullOrWhiteSpace(exception.ApprovalReference) &&
                exception.ApprovedBy != Guid.Empty && exception.ApprovedAtUtc.Offset == TimeSpan.Zero &&
                exception.ExpiresAtUtc.Offset == TimeSpan.Zero && exception.ApprovedAtUtc <= nowUtc &&
                exception.ExpiresAtUtc > nowUtc && exception.ExpiresAtUtc > exception.ApprovedAtUtc;
            return CreateResult(definition, evidence,
                isApproved ? AssessmentResultState.Exception : AssessmentResultState.UnableToAssess,
                isApproved ? $"Assessment is covered by approved exception {exception!.ExceptionId}." :
                    "Exception status has no active, tenant-bound approval.",
                [], expectedCount, coveredCount);
        }

        if (evidence.CollectionOutcome != CollectionOutcome.Complete)
        {
            var reason = evidence.CollectionOutcome switch
            {
                CollectionOutcome.Manual => "Evidence requires manual review.",
                CollectionOutcome.Denied => "Source denied the read operation.",
                CollectionOutcome.Partial => "Collection was incomplete.",
                CollectionOutcome.Stale => "Evidence is marked stale.",
                CollectionOutcome.Unsupported => "The source operation is unsupported.",
                CollectionOutcome.UnsupportedCloud => "The customer cloud is unsupported.",
                CollectionOutcome.MissingLicense => "A required license is unavailable.",
                CollectionOutcome.MissingPermission => "A required read permission is unavailable.",
                CollectionOutcome.Throttled => "Collection stopped after throttling limits.",
                CollectionOutcome.Error => "Collection failed without a trustworthy result.",
                _ => "Collection did not complete."
            };
            return CreateResult(definition, evidence,
                evidence.CollectionOutcome == CollectionOutcome.Manual
                    ? AssessmentResultState.ManualReview
                    : AssessmentResultState.UnableToAssess,
                reason, [], expectedCount, coveredCount);
        }

        if (definition.MaximumEvidenceAge > TimeSpan.Zero &&
            nowUtc - evidence.CollectedAtUtc > definition.MaximumEvidenceAge)
        {
            return CreateResult(definition, evidence, AssessmentResultState.UnableToAssess,
                "Evidence exceeds the control's maximum age.", [], expectedCount, coveredCount);
        }

        return null;
    }

    private static string? ValidateInventories(
        AssessmentControlDefinition definition,
        NormalizedEvidence evidence,
        int expectedCount,
        int coveredCount)
    {
        if (expectedCount <= 0)
        {
            return "The approved expected inventory is empty; a zero-sized denominator cannot pass.";
        }

        foreach (var inventoryName in definition.RequiredInventories)
        {
            if (!evidence.ExpectedInventories.TryGetValue(inventoryName, out var inventory) ||
                !inventory.Approved || string.IsNullOrWhiteSpace(inventory.Source) || inventory.ResourceIds.Count == 0)
            {
                return $"Expected inventory '{inventoryName}' is missing, unapproved, or empty.";
            }

            if (inventory.OrganizationId != evidence.OrganizationId || inventory.CustomerTenantId != evidence.CustomerTenantId ||
                inventory.CapturedAtUtc.Offset != TimeSpan.Zero || inventory.CapturedAtUtc > evidence.CollectedAtUtc ||
                inventory.ResourceIds.Distinct(StringComparer.Ordinal).Count() != inventory.ResourceIds.Count)
            {
                return $"Expected inventory '{inventoryName}' has invalid tenant binding, timestamp, or duplicate IDs.";
            }

            if (!evidence.CoveredInventoryResources.TryGetValue(inventoryName, out var covered) ||
                covered.Distinct(StringComparer.Ordinal).Count() != covered.Count ||
                !inventory.ResourceIds.ToHashSet(StringComparer.Ordinal).SetEquals(covered))
            {
                return $"Collection coverage for expected inventory '{inventoryName}' is incomplete.";
            }
        }

        return null;
    }

    private static AssessmentDelta EvaluateRule(ControlRule rule, NormalizedEvidence evidence)
    {
        if (!evidence.CurrentState.TryGetValue(rule.Field, out var current))
        {
            return new AssessmentDelta(rule.Field, ExpectedDescription(rule), null, null, "Current value is absent.");
        }

        bool? matches = rule.Operator switch
        {
            RuleOperator.EqualsLiteral when rule.Literal is not null => ValuesEqual(current, rule.Literal),
            RuleOperator.EqualsField when rule.RelatedField is not null && evidence.CurrentState.TryGetValue(rule.RelatedField, out var related) => ValuesEqual(current, related),
            RuleOperator.EqualsExpectedInventory when rule.RelatedField is not null && evidence.ExpectedInventories.TryGetValue(rule.RelatedField, out var inventory) =>
                StringSetEquals(current, inventory.ResourceIds),
            RuleOperator.IsSubsetOfField when rule.RelatedField is not null && evidence.CurrentState.TryGetValue(rule.RelatedField, out var superset) =>
                StringSetIsSubset(current, superset),
            RuleOperator.IsEmpty => IsStringSet(current) && current.Value.GetArrayLength() == 0,
            RuleOperator.IsNonEmpty => IsStringSet(current) && current.Value.GetArrayLength() > 0,
            RuleOperator.IsTrue => current.Kind == NormalizedValueKind.Boolean && current.Value.GetBoolean(),
            RuleOperator.IsFalse => current.Kind == NormalizedValueKind.Boolean && !current.Value.GetBoolean(),
            RuleOperator.IsZero => current.Kind == NormalizedValueKind.Number && current.Value.GetDecimal() == 0,
            _ => null
        };

        return new AssessmentDelta(rule.Field, ExpectedDescription(rule), current, matches,
            matches switch { true => "Expected condition observed.", false => "Expected condition not observed.", _ => "Rule cannot be evaluated from available typed evidence." });
    }

    private static void ValidateEvidenceIdentity(AssessmentControlDefinition definition, NormalizedEvidence evidence)
    {
        if (evidence.ContractVersion != AssessmentContractVersions.NormalizedEvidence ||
            evidence.OrganizationId == Guid.Empty || evidence.CustomerTenantId == Guid.Empty ||
            evidence.CollectedAtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(evidence.CollectionOutcome) ||
            evidence.CurrentState is null || evidence.EvidenceReferences is null ||
            evidence.ExpectedInventories is null || evidence.CoveredInventoryResources is null)
        {
            throw new AssessmentContractException("Evidence version, tenant identifiers, UTC collection time, and collection outcome are required.");
        }

        if (evidence.BaselinePackId != definition.BaselinePackId ||
            evidence.BaselinePackVersion != definition.BaselinePackVersion ||
            evidence.BaselinePackHash != definition.BaselinePackHash ||
            evidence.ControlId != definition.Id || evidence.Service != definition.Service ||
            evidence.SourceUrl != definition.SourceUrl || evidence.SourceRevision != definition.SourceRevision ||
            evidence.SourceHash != definition.SourceHash || string.IsNullOrWhiteSpace(evidence.AdapterId) ||
            string.IsNullOrWhiteSpace(evidence.AdapterVersion))
        {
            throw new AssessmentContractException("Evidence does not match the pinned control, source, service, or adapter identity.");
        }
    }

    private static AssessmentControlResult CreateResult(
        AssessmentControlDefinition definition,
        NormalizedEvidence evidence,
        AssessmentResultState state,
        string reason,
        IReadOnlyList<AssessmentDelta> deltas,
        int expectedCount,
        int coveredCount) =>
        new(AssessmentContractVersions.NormalizedEvidence, AssessmentContractVersions.Evaluator,
            definition.BaselinePackId, definition.BaselinePackVersion, definition.BaselinePackHash,
            evidence.OrganizationId, evidence.CustomerTenantId,
            definition.Id, definition.Service, state, reason, evidence.CurrentState, evidence.EvidenceReferences,
            definition.Rules, deltas, Math.Max(expectedCount, 0), Math.Max(coveredCount, 0), definition.SupportMode,
            definition.SourceReviewed, definition.RightsApproved,
            definition.SourceUrl, definition.SourceRevision,
            definition.SourceHash, evidence.AdapterId, evidence.AdapterVersion, evidence.CollectedAtUtc);

    private static bool ValuesEqual(NormalizedValue left, NormalizedValue right)
        => NormalizedValueSemantics.AreEquivalent(left, right);

    private static bool StringSetEquals(NormalizedValue value, IEnumerable<string> expected) =>
        IsStringSet(value) && value.Value.EnumerateArray().Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal).SetEquals(expected);

    private static bool StringSetIsSubset(NormalizedValue subset, NormalizedValue superset) =>
        IsStringSet(subset) && IsStringSet(superset) &&
        subset.Value.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            .IsSubsetOf(superset.Value.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal));

    private static bool IsStringSet(NormalizedValue value) => value.Kind == NormalizedValueKind.StringSet &&
        value.Value.ValueKind == JsonValueKind.Array && value.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);

    private static string ExpectedDescription(ControlRule rule) =>
        rule.Description ?? rule.Operator switch
        {
            RuleOperator.EqualsLiteral => $"equals {rule.Literal?.Value.ToString()}",
            RuleOperator.EqualsField => $"equals field {rule.RelatedField}",
            RuleOperator.EqualsExpectedInventory => $"equals approved inventory {rule.RelatedField}",
            RuleOperator.IsSubsetOfField => $"is a subset of {rule.RelatedField}",
            RuleOperator.IsEmpty => "is empty",
            RuleOperator.IsNonEmpty => "is non-empty",
            RuleOperator.IsTrue => "is true",
            RuleOperator.IsFalse => "is false",
            RuleOperator.IsZero => "is zero",
            _ => "has expected value"
        };
}