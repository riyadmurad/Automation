using System.Text.Json.Nodes;

namespace M365.SecurityAssessment.Domain;

public static class BaselineInventoryValidator
{
    public static void Validate(BaselineInventoryDocument inventory)
    {
        if (inventory.SchemaVersion != ContractVersions.BaselineInventory)
        {
            throw new UnsupportedContractVersionException("baseline inventory", inventory.SchemaVersion);
        }

        if (inventory.Controls.Count != InitialControlIds.All.Length ||
            !inventory.Controls.Select(control => control.Id).SequenceEqual(InitialControlIds.All))
        {
            throw new ContractValidationException("The initial inventory must contain the 13 canonical control IDs in order.");
        }

        foreach (var control in inventory.Controls)
        {
            if (!Uri.TryCreate(control.SourceUrl, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ContractValidationException($"Control {control.Id} must have an HTTPS source URL.");
            }

            if (control.ReviewStatus != "unverified" || control.SupportMode != "manual/unverified" ||
                control.SourceRevision is not null || control.ContentHash is not null)
            {
                throw new ContractValidationException($"Control {control.Id} cannot claim reviewed support or unverified provenance.");
            }

            if (control.NormalizedFields.Length == 0 ||
                !control.NormalizedFields.Order(StringComparer.Ordinal)
                    .SequenceEqual(control.ExpectedState.Keys.Order(StringComparer.Ordinal)))
            {
                throw new ContractValidationException($"Control {control.Id} must define expected values for every normalized field.");
            }

            if (control.SetFields.Any(field => !control.ExpectedState.TryGetValue(field, out var value) ||
                value.ValueKind != System.Text.Json.JsonValueKind.Array ||
                value.EnumerateArray().Any(item => item.ValueKind != System.Text.Json.JsonValueKind.String)))
            {
                throw new ContractValidationException($"Control {control.Id} set fields must be declared string arrays in its expected state.");
            }

            if (control.ResourceIdField is not null &&
                (!control.NormalizedFields.Contains(control.ResourceIdField, StringComparer.Ordinal) ||
                 !control.SetFields.Contains(control.ResourceIdField, StringComparer.Ordinal)))
            {
                throw new ContractValidationException($"Control {control.Id} resource ID field must be a normalized set field.");
            }

            var relatedSetFields = control.SetEqualityFields.SelectMany(pair => new[] { pair.Key, pair.Value })
                .Concat(control.SetSubsetFields.SelectMany(pair => new[] { pair.Key, pair.Value }))
                .Concat(control.EmptySetFields).Concat(control.NonEmptySetFields);
            if (relatedSetFields.Any(field => !control.SetFields.Contains(field, StringComparer.Ordinal)) ||
                control.SetEqualityFields.Keys.Intersect(control.SetSubsetFields.Keys, StringComparer.Ordinal).Any() ||
                control.EmptySetFields.Intersect(control.NonEmptySetFields, StringComparer.Ordinal).Any())
            {
                throw new ContractValidationException($"Control {control.Id} set relationships must reference distinct declared set fields.");
            }
        }
    }
}

public static class SyntheticFixtureValidator
{
    public static void Validate(SyntheticFixtureDocument fixture, BaselineInventoryDocument inventory)
    {
        if (fixture.SchemaVersion != ContractVersions.SyntheticFixture)
        {
            throw new UnsupportedContractVersionException("synthetic fixture", fixture.SchemaVersion);
        }

        if (fixture.Source != "synthetic" || fixture.LiveConnections || fixture.WritesEnabled)
        {
            throw new ContractValidationException("Synthetic fixtures cannot enable live connections or writes.");
        }

        if (fixture.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(fixture.CustomerName) ||
            fixture.CollectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ContractValidationException("Synthetic tenant identity and UTC collection time are required.");
        }

        var ids = fixture.Controls.Select(control => control.ControlId).ToArray();
        if (fixture.Controls.Count != inventory.Controls.Count ||
            ids.Distinct(StringComparer.Ordinal).Count() != inventory.Controls.Count ||
            !ids.Order(StringComparer.Ordinal).SequenceEqual(InitialControlIds.All.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new ContractValidationException("Each synthetic customer fixture must contain every initial control exactly once.");
        }

        var definitions = inventory.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        foreach (var controlFixture in fixture.Controls)
        {
            if (!Enum.IsDefined(controlFixture.CollectionState))
            {
                throw new ContractValidationException($"Fixture collection state for {controlFixture.ControlId} is not defined.");
            }

            var definition = definitions[controlFixture.ControlId];
            var observedFields = controlFixture.ObservedState.Keys.Order(StringComparer.Ordinal);
            if (controlFixture.CollectionState == CollectionState.Complete &&
                !observedFields.SequenceEqual(definition.NormalizedFields.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new ContractValidationException($"Complete fixture evidence for {controlFixture.ControlId} must include every normalized field.");
            }

            if (controlFixture.CollectionState == CollectionState.Complete && definition.SetFields.Any(field =>
                controlFixture.ObservedState[field].ValueKind != System.Text.Json.JsonValueKind.Array ||
                controlFixture.ObservedState[field].EnumerateArray().Any(item => item.ValueKind != System.Text.Json.JsonValueKind.String)))
            {
                throw new ContractValidationException($"Complete fixture set fields for {controlFixture.ControlId} must contain string arrays.");
            }

            if (controlFixture.CollectionState == CollectionState.Complete && definition.ResourceIdField is { } resourceIdField)
            {
                var expectedIds = controlFixture.ObservedState[resourceIdField].EnumerateArray()
                    .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
                if (controlFixture.ResourceIds.Distinct(StringComparer.Ordinal).Count() != controlFixture.ResourceIds.Length ||
                    !expectedIds.SetEquals(controlFixture.ResourceIds))
                {
                    throw new ContractValidationException($"Resource IDs for {controlFixture.ControlId} must equal its declared expected scope.");
                }
            }

            if (controlFixture.CollectionState != CollectionState.Complete &&
                controlFixture.ObservedState.Keys.Except(definition.NormalizedFields, StringComparer.Ordinal).Any())
            {
                throw new ContractValidationException($"Fixture evidence for {controlFixture.ControlId} contains an unknown normalized field.");
            }
        }
    }
}

public static class SyntheticControlEvaluator
{
    public static SyntheticControlEvaluation Evaluate(
        ControlDefinition definition, SyntheticControlFixture fixture, Guid tenantId, DateTimeOffset collectedAtUtc)
    {
        if (!Enum.IsDefined(fixture.CollectionState))
        {
            throw new ContractValidationException($"Fixture collection state for {fixture.ControlId} is not defined.");
        }

        var (status, reason, simulationMatchesExpected) = fixture.CollectionState switch
        {
            CollectionState.Denied => (AssessmentStatus.UnableToAssess, EvaluationReason.CollectionDenied, (bool?)null),
            CollectionState.Partial => (AssessmentStatus.UnableToAssess, EvaluationReason.CollectionPartial, (bool?)null),
            CollectionState.Stale => (AssessmentStatus.UnableToAssess, EvaluationReason.EvidenceStale, (bool?)null),
            CollectionState.Unsupported => (AssessmentStatus.UnableToAssess, EvaluationReason.ServiceUnsupported, (bool?)null),
            CollectionState.MissingLicense => (AssessmentStatus.UnableToAssess, EvaluationReason.LicenseUnavailable, (bool?)null),
            CollectionState.Manual => (AssessmentStatus.ManualReview, EvaluationReason.ManualEvidenceRequired, (bool?)null),
            CollectionState.Complete => EvaluateComplete(definition, fixture),
            _ => throw new ContractValidationException($"Fixture collection state for {fixture.ControlId} is not supported.")
        };

        return new SyntheticControlEvaluation(
            definition.Id, status, reason, definition.SourceUrl, definition.ReviewStatus,
            definition.SupportMode, definition.SourceRevision,
            fixture.CollectionState == CollectionState.Complete ? BuildExpectedState(definition, fixture) : definition.ExpectedState,
            fixture.ObservedState, fixture.ResourceIds, collectedAtUtc,
            [$"synthetic://{tenantId:D}/{definition.Id}"], simulationMatchesExpected);
    }

    private static (AssessmentStatus Status, EvaluationReason Reason, bool? SimulationMatchesExpected) EvaluateComplete(
        ControlDefinition definition, SyntheticControlFixture fixture)
    {
        if (!definition.ExpectedState.Keys.All(fixture.ObservedState.ContainsKey))
        {
            return (AssessmentStatus.UnableToAssess, EvaluationReason.RequiredEvidenceMissing, null);
        }

        var matches = MatchesExpectedState(definition, fixture);

        if (definition.ReviewStatus == "unverified" || definition.SupportMode == "manual/unverified")
        {
            return (AssessmentStatus.ManualReview, EvaluationReason.ManualEvidenceRequired, matches);
        }

        return matches
            ? (AssessmentStatus.Compliant, EvaluationReason.ExpectedStateObserved, true)
            : (AssessmentStatus.NonCompliant, EvaluationReason.ExpectedStateMismatch, false);
    }

    private static bool JsonSetEquals(System.Text.Json.JsonElement expected, System.Text.Json.JsonElement observed)
    {
        if (!IsStringSet(expected) || !IsStringSet(observed))
        {
            return false;
        }

        var expectedValues = expected.EnumerateArray().Select(item => item.GetString()!).ToArray();
        var observedValues = observed.EnumerateArray().Select(item => item.GetString()!).ToArray();
        return expectedValues.Distinct(StringComparer.Ordinal).Count() == expectedValues.Length &&
            observedValues.Distinct(StringComparer.Ordinal).Count() == observedValues.Length &&
            expectedValues.ToHashSet(StringComparer.Ordinal).SetEquals(observedValues);
    }

    private static bool MatchesExpectedState(ControlDefinition definition, SyntheticControlFixture fixture)
    {
        if (definition.ResourceIdField is { } resourceIdField &&
            (!fixture.ObservedState.TryGetValue(resourceIdField, out var resourceIds) ||
             !JsonSetEquals(resourceIds, System.Text.Json.JsonSerializer.SerializeToElement(fixture.ResourceIds))))
        {
            return false;
        }

        var dynamicFields = definition.SetEqualityFields.Values.Concat(definition.SetSubsetFields.Values)
            .Concat(definition.NonEmptySetFields)
            .Append(definition.ResourceIdField ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pair in definition.ExpectedState)
        {
            if (!fixture.ObservedState.TryGetValue(pair.Key, out var observed))
            {
                return false;
            }

            if (definition.EmptySetFields.Contains(pair.Key, StringComparer.Ordinal))
            {
                if (!IsStringSet(observed) || observed.GetArrayLength() != 0)
                {
                    return false;
                }
                continue;
            }

            if (definition.SetEqualityFields.TryGetValue(pair.Key, out var equalField))
            {
                if (!fixture.ObservedState.TryGetValue(equalField, out var equalTo) || !JsonSetEquals(observed, equalTo))
                {
                    return false;
                }
                continue;
            }

            if (definition.SetSubsetFields.TryGetValue(pair.Key, out var subsetField))
            {
                if (!fixture.ObservedState.TryGetValue(subsetField, out var subsetOf) || !JsonSetIsSubset(observed, subsetOf))
                {
                    return false;
                }
                continue;
            }

            if (dynamicFields.Contains(pair.Key))
            {
                if (definition.NonEmptySetFields.Contains(pair.Key, StringComparer.Ordinal) &&
                    (!IsStringSet(observed) || observed.GetArrayLength() == 0))
                {
                    return false;
                }
                continue;
            }

            var matches = definition.SetFields.Contains(pair.Key, StringComparer.Ordinal)
                ? JsonSetEquals(pair.Value, observed)
                : JsonNode.DeepEquals(JsonNode.Parse(pair.Value.GetRawText()), JsonNode.Parse(observed.GetRawText()));
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonSetIsSubset(System.Text.Json.JsonElement subset, System.Text.Json.JsonElement superset) =>
        IsStringSet(subset) && IsStringSet(superset) &&
        subset.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            .IsSubsetOf(superset.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal));

    private static bool IsStringSet(System.Text.Json.JsonElement value) =>
        value.ValueKind == System.Text.Json.JsonValueKind.Array &&
        value.EnumerateArray().All(item => item.ValueKind == System.Text.Json.JsonValueKind.String);

    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> BuildExpectedState(
        ControlDefinition definition, SyntheticControlFixture fixture)
    {
        var expected = definition.ExpectedState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (definition.ResourceIdField is { } resourceIdField && fixture.ObservedState.TryGetValue(resourceIdField, out var resourceIds))
        {
            expected[resourceIdField] = resourceIds;
        }

        foreach (var field in definition.SetEqualityFields.Values.Concat(definition.SetSubsetFields.Values))
        {
            if (fixture.ObservedState.TryGetValue(field, out var observed))
            {
                expected[field] = observed;
            }
        }

        foreach (var pair in definition.SetEqualityFields)
        {
            if (fixture.ObservedState.TryGetValue(pair.Value, out var requiredInventory))
            {
                expected[pair.Key] = requiredInventory;
            }
        }

        foreach (var pair in definition.SetSubsetFields)
        {
            if (fixture.ObservedState.TryGetValue(pair.Key, out var subset) && IsStringSet(subset) &&
                fixture.ObservedState.TryGetValue(pair.Value, out var allowed) && IsStringSet(allowed))
            {
                expected[pair.Key] = System.Text.Json.JsonSerializer.SerializeToElement(
                    subset.EnumerateArray().Select(item => item.GetString()!)
                        .Intersect(allowed.EnumerateArray().Select(item => item.GetString()!), StringComparer.Ordinal).ToArray());
            }
        }

        return expected;
    }
}

public sealed class SyntheticFixtureCatalog
{
    private readonly BaselineInventoryDocument _inventory;
    private readonly IReadOnlyList<SyntheticFixtureDocument> _fixtures;

    private SyntheticFixtureCatalog(BaselineInventoryDocument inventory, IReadOnlyList<SyntheticFixtureDocument> fixtures)
    {
        _inventory = inventory;
        _fixtures = fixtures;
    }

    public static SyntheticFixtureCatalog Load(string inventoryPath, string fixtureDirectory)
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(inventoryPath);
        BaselineInventoryValidator.Validate(inventory);
        var fixtures = Directory.EnumerateFiles(fixtureDirectory, "*.json")
            .Order(StringComparer.Ordinal).Select(ContractJson.Load<SyntheticFixtureDocument>).ToArray();

        if (fixtures.Length != 2)
        {
            throw new ContractValidationException("Exactly two synthetic customer fixtures are required.");
        }

        foreach (var fixture in fixtures)
        {
            SyntheticFixtureValidator.Validate(fixture, inventory);
        }

        if (fixtures.Select(fixture => fixture.TenantId).Distinct().Count() != 2)
        {
            throw new ContractValidationException("Synthetic customer fixtures must use different tenant IDs.");
        }

        return new SyntheticFixtureCatalog(inventory, fixtures);
    }

    public IReadOnlyList<SyntheticFixtureSummary> DescribeFixtures() => _fixtures.Select(fixture =>
        new SyntheticFixtureSummary(ContractVersions.ApiResponse, "synthetic", false, false,
            fixture.TenantId, fixture.CustomerName, fixture.Controls.Count)).ToArray();

    public SyntheticAssessmentResponse? Evaluate(Guid tenantId)
    {
        var fixture = _fixtures.SingleOrDefault(item => item.TenantId == tenantId);
        if (fixture is null)
        {
            return null;
        }

        var definitions = _inventory.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        var results = fixture.Controls.Select(control => SyntheticControlEvaluator.Evaluate(
            definitions[control.ControlId], control, fixture.TenantId, fixture.CollectedAtUtc)).ToArray();
        return new SyntheticAssessmentResponse(ContractVersions.ApiResponse, "synthetic", false, false, "synthetic-fixture",
            fixture.TenantId, fixture.CustomerName, fixture.CollectedAtUtc, results);
    }
}