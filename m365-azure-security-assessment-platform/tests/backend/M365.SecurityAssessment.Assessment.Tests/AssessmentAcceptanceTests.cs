using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using M365.SecurityAssessment.Assessment;

namespace M365.SecurityAssessment.Assessment.Tests;

public sealed class AssessmentAcceptanceTests
{
    private static readonly DateTimeOffset CollectedAt = DateTimeOffset.Parse("2026-11-02T10:15:00Z");
    private static readonly DateTimeOffset Now = CollectedAt.AddMinutes(1);
    private const string FrozenPackSha256 = "D52A248899C3C3B27FCA91C738AAC6C237978E3EA0618F47CE9E96F692168AE4";
    private static readonly string[] FixtureCaseNames =
    [
        "matching-candidate", "mismatching-candidate", "denied", "partial", "stale", "unsupported",
        "unsupported-cloud", "missing-license", "missing-permission", "throttled", "error", "manual",
        "missing-field", "missing-inventory"
    ];
    private static readonly BaselinePackDocument Pack = BaselinePackJson.Load(
        Path.Combine(AppContext.BaseDirectory, "qualified-baseline", "pack-0.1.1.json"));

    public static TheoryData<string, string> ControlFixtureCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var control in Pack.Controls)
            {
                foreach (var fixtureCase in FixtureCaseNames)
                {
                    data.Add(control.Id, fixtureCase);
                }
            }
            return data;
        }
    }

    public static TheoryData<string> InitialControlIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var control in Pack.Controls)
            {
                data.Add(control.Id);
            }
            return data;
        }
    }

    public static TheoryData<string, string> FoundationQualifiedFixtureCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            var cases = new[]
            {
                "matching-candidate", "mismatching-candidate", "denied", "partial",
                "stale", "unsupported", "missing-license", "manual"
            };
            foreach (var control in Pack.Controls)
            {
                foreach (var fixtureCase in cases)
                {
                    data.Add(control.Id, fixtureCase);
                }
            }
            return data;
        }
    }

    [Fact]
    public void QualifiedPackPinsAllControlsSourcesAndTraceabilityWithoutClaimingAutomation()
    {
        BaselinePackValidator.Validate(Pack);
        Assert.Equal(13, Pack.Controls.Count);
        Assert.Equal(13, Pack.Sources.Count);
        Assert.All(Pack.Sources, source =>
        {
            Assert.False(string.IsNullOrWhiteSpace(source.SourceOrganization));
            Assert.StartsWith("https://", source.RequestedUrl, StringComparison.Ordinal);
            Assert.StartsWith("https://", source.ResolvedUrl, StringComparison.Ordinal);
            Assert.Equal(200, source.HttpStatus);
            Assert.Equal("retrieved", source.RetrievalStatus);
            Assert.Equal(TimeSpan.Zero, source.RetrievedAtUtc.Offset);
            Assert.True(source.ResponseByteLength > 0);
            Assert.Equal($"sha256:{source.ResponseSha256}", source.SourceRevision);
            Assert.Matches("^[0-9a-f]{64}$", source.ResponseSha256);
            Assert.False(source.ContentIncluded);
            Assert.False(string.IsNullOrWhiteSpace(source.ReviewNote));
            Assert.Equal(ReviewState.PendingReview, source.ReviewState);
        });
        Assert.All(Pack.Controls, control =>
        {
            Assert.Equal(ReviewState.PendingReview, control.ReviewState);
            Assert.False(control.RightsApproved);
            Assert.False(control.OperationQualified);
            Assert.Equal("manual/unverified", control.SupportMode);
            Assert.StartsWith($"control_{control.Id}_", control.FixtureFamily, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(control.SpecificationScenario));
            Assert.Matches("^(8|9|10|11)\\.[1-7]$", control.ImplementationTask);
            Assert.NotEmpty(control.RequiredFields);
            Assert.NotEmpty(control.Rules);
            Assert.All(control.PermissionPrerequisites, prerequisite => Assert.False(string.IsNullOrWhiteSpace(prerequisite)));
            Assert.All(control.LicensePrerequisites, prerequisite => Assert.False(string.IsNullOrWhiteSpace(prerequisite)));
            Assert.All(control.RuntimePrerequisites, prerequisite => Assert.False(string.IsNullOrWhiteSpace(prerequisite)));
            Assert.False(string.IsNullOrWhiteSpace(control.ExpectedStateReviewNote));
        });
        var traceability = new Dictionary<string, (string Scenario, string Task)>(StringComparer.Ordinal)
        {
            ["ENT-001"] = ("ENT-001 privileged roles are covered", "8.1"),
            ["EXO-001"] = ("EXO-001 external forwarding is disabled", "8.3"),
            ["TEAM-001"] = ("TEAM-001 anonymous meeting join is disabled", "8.4"),
            ["SPO-001"] = ("SPO-001 default links use organization view access", "8.6"),
            ["PP-001"] = ("PP-001 DLP covers expected environments", "9.1"),
            ["INT-001"] = ("INT-001 expected devices are compliant", "9.3"),
            ["MDI-001"] = ("MDI-001 expected domain controllers have healthy sensors", "10.1"),
            ["MDO-001"] = ("MDO-001 Safe Links covers expected recipients", "10.2"),
            ["MDE-001"] = ("MDE-001 expected devices have tamper protection", "10.3"),
            ["MDCA-001"] = ("MDCA-001 governance policies cover expected apps", "10.4"),
            ["XDR-001"] = ("XDR-001 expected licensed products are integrated", "10.5"),
            ["PUR-001"] = ("PUR-001 unified audit logging is enabled", "9.5"),
            ["AZR-001"] = ("AZR-001 expected storage accounts require secure transfer", "11.1")
        };
        Assert.Equal(Pack.Controls.Count, traceability.Count);
        foreach (var control in Pack.Controls)
        {
            Assert.Equal(traceability[control.Id].Scenario, control.SpecificationScenario);
            Assert.Equal(traceability[control.Id].Task, control.ImplementationTask);
            Assert.Equal($"control_{control.Id}_<case>", control.FixtureFamily);
        }
        Assert.False(Pack.ContainsSourceText);
        Assert.False(Pack.ContainsExecutableContent);
    }

    [Fact]
    public void FreezeManifestPinsPackBytesAndAcceptanceMatrix()
    {
        var baselineDirectory = Path.Combine(AppContext.BaseDirectory, "qualified-baseline");
        var packPath = Path.Combine(baselineDirectory, "pack-0.1.1.json");
        var manifestPath = Path.Combine(baselineDirectory, "freeze-manifest-0.1.1.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        var packBytes = File.ReadAllBytes(packPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(packBytes));

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(Pack.PackId, root.GetProperty("packId").GetString());
        Assert.Equal("0.1.1", Pack.Version);
        Assert.Equal(Pack.Version, root.GetProperty("packVersion").GetString());
        Assert.Equal("pack-0.1.1.json", root.GetProperty("packFile").GetString());
        Assert.Equal("pack-0.1.1.json", root.GetProperty("traceabilitySource").GetString());
        Assert.Equal("pack-0.1.1.json", root.GetProperty("sourceProvenanceSource").GetString());
        Assert.Equal(FrozenPackSha256, root.GetProperty("packFileSha256").GetString());
        Assert.Equal(FrozenPackSha256, actualHash);
        Assert.Equal(Pack.Controls.Select(control => control.Id),
            root.GetProperty("controlIds").EnumerateArray().Select(id => id.GetString()));

        var requiredCases = new[]
        {
            "matching-candidate", "mismatching-candidate", "denied", "partial", "stale", "unsupported",
            "missing-license", "manual"
        };
        Assert.Equal(requiredCases,
            root.GetProperty("requiredAcceptanceCases").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(14, FixtureCaseNames.Length);
        Assert.All(requiredCases, fixtureCase => Assert.Contains(fixtureCase, FixtureCaseNames));
        Assert.Equal(Pack.Controls.Count * FixtureCaseNames.Length, ControlFixtureCases.Count);
    }

    [Theory]
    [MemberData(nameof(InitialControlIds))]
    public void EveryControlFixtureCarriesAllDeclaredFieldsAndTraceableEvidence(string controlId)
    {
        var control = Pack.Controls.Single(item => item.Id == controlId);
        var evidence = ControlEvidenceFixture.Create(control, Pack, "matching-candidate");

        Assert.Equal(AssessmentContractVersions.NormalizedEvidence, evidence.ContractVersion);
        Assert.Equal(Pack.Version, evidence.BaselinePackVersion);
        Assert.Equal(CanonicalContentHash.Compute(Pack), evidence.BaselinePackHash);
        Assert.Equal(control.RequiredFields.Order(StringComparer.Ordinal), evidence.CurrentState.Keys.Order(StringComparer.Ordinal));
        Assert.All(control.RequiredFields, field => evidence.CurrentState[field].Validate($"{control.Id}.{field}"));
        Assert.NotEmpty(evidence.EvidenceReferences);
        Assert.Contains(Pack.Sources.Single(source => source.Id == control.SourceId).ResolvedUrl, evidence.SourceUrl);
        Assert.Matches("^[0-9a-f]{64}$", evidence.SourceHash);
        Assert.Equal("synthetic-fixture-adapter", evidence.AdapterId);
        Assert.Equal("0.1.0", evidence.AdapterVersion);
        Assert.Equal(CustomerTenantId, evidence.CustomerTenantId);
        Assert.Equal(OrganizationId, evidence.OrganizationId);
    }

    [Fact]
    public void EvidenceVersionTenantProvenanceAndInventoryGapsFailClosed()
    {
        var control = Pack.Controls.Single(item => item.Id == "INT-001");
        var evidence = ControlEvidenceFixture.Create(control, Pack, "matching-candidate");
        var evaluator = new AssessmentEvaluator();

        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { ContractVersion = "0.9" }, Now));
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { BaselinePackVersion = "0.0.1" }, Now));
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { OrganizationId = Guid.Empty }, Now));
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { SourceRevision = "sha256:" + new string('0', 64) }, Now));
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { SourceHash = new string('0', 64) }, Now));

        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack,
            evidence with { CustomerTenantId = Guid.NewGuid() }, Now).State);
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack,
            evidence with { EvidenceReferences = [] }, Now).State);
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack,
            evidence with { ExpectedInventories = new Dictionary<string, ExpectedInventory>() }, Now).State);
    }

    [Fact]
    public void SourceDiscoveryErrorsAreRecordedButNeverAcceptedAsPackSources()
    {
        using var failures = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "qualified-baseline", "source-discovery-failures.json")));
        var records = failures.RootElement.GetProperty("failures").EnumerateArray().ToArray();
        Assert.Equal(3, records.Length);
        Assert.All(records, record =>
        {
            Assert.Equal(404, record.GetProperty("httpStatus").GetInt32());
            Assert.Equal("59236e9bc876ab761e6acb34961223f13d200cb7fabae0e60b8d2280dd25ffb9",
                record.GetProperty("responseSha256").GetString());
        });
        Assert.DoesNotContain(Pack.Sources, source => source.HttpStatus == 404);
    }

    [Fact]
    public void PackRejectsSourceTextAndUnsupportedAutomationClaims()
    {
        Assert.Throws<AssessmentContractException>(() => BaselinePackValidator.Validate(Pack with { ContainsSourceText = true }));
        Assert.Throws<AssessmentContractException>(() => BaselinePackValidator.Validate(Pack with { SchemaVersion = "0.9" }));
        Assert.Throws<AssessmentContractException>(() => BaselinePackValidator.Validate(Pack with { Version = "not-semver" }));
        var first = Pack.Controls[0] with { SupportMode = "automated/verified", OperationQualified = false };
        var invalid = Pack with { Controls = [first, .. Pack.Controls.Skip(1)] };
        Assert.Throws<AssessmentContractException>(() => BaselinePackValidator.Validate(invalid));
    }

    [Fact]
    public void PackVersionsAreImmutableAndChangedContentNeedsANewVersion()
    {
        var catalog = new ImmutableBaselineCatalog();
        var first = catalog.Publish(Pack);
        var altered = Pack with
        {
            Controls = [Pack.Controls[0] with { ExpectedStateReviewNote = "changed" }, .. Pack.Controls.Skip(1)]
        };
        Assert.Throws<AssessmentContractException>(() => catalog.Publish(altered));
        var next = catalog.Publish(altered with { Version = "0.1.2" });
        Assert.NotEqual(first.ContentHash, next.ContentHash);
        Assert.Equal(Pack.Version, catalog.Get(Pack.PackId, Pack.Version).Version);
    }

    [Fact]
    public void PendingPackCannotActivateEvenWithAnApprovalBoundToItsHash()
    {
        var catalog = new ImmutableBaselineCatalog();
        var release = catalog.Publish(Pack);
        var approval = new BaselineApproval("approval-1", release.ContentHash, Guid.NewGuid(), Now,
            "customer-scope", "baseline-activation");
        Assert.Throws<AssessmentContractException>(() => catalog.Activate(Pack.PackId, Pack.Version, approval));
        Assert.False(catalog.IsActive(Pack.PackId, Pack.Version, release.ContentHash));
    }

    [Fact]
    public void ApprovedPackActivationRetainsItsExactImmutableApproval()
    {
        var approvedPack = Pack with
        {
            ReviewState = ReviewState.Approved,
            Sources = Pack.Sources.Select(source => source with { ReviewState = ReviewState.Approved }).ToArray(),
            Controls = Pack.Controls.Select(control => control with
            {
                ExpectedStateClassification = control.ExpectedStateClassification == ExpectedStateClassification.Unresolved
                    ? ExpectedStateClassification.CustomerPolicyCandidate
                    : control.ExpectedStateClassification,
                ReviewState = ReviewState.Approved,
                RightsApproved = true
            }).ToArray()
        };
        var catalog = new ImmutableBaselineCatalog();
        var release = catalog.Publish(approvedPack);
        var approval = new BaselineApproval("baseline-activation-1", release.ContentHash, Guid.NewGuid(), Now,
            "synthetic-test-scope", "baseline-activation");
        catalog.Activate(approvedPack.PackId, approvedPack.Version, approval);
        Assert.True(catalog.IsActive(approvedPack.PackId, approvedPack.Version, release.ContentHash));
        Assert.Equal(approval, catalog.GetActivationApproval(approvedPack.PackId, approvedPack.Version, release.ContentHash));
        Assert.Throws<AssessmentContractException>(() => catalog.Activate(approvedPack.PackId, approvedPack.Version,
            approval with { ApprovalId = "replacement-approval" }));
    }

    [Fact]
    public void SourceUpdateCreatesReviewNoticeAndDoesNotActivate()
    {
        var source = Pack.Sources[1];
        var observed = source with
        {
            ResponseSha256 = new string('a', 64),
            SourceRevision = $"sha256:{new string('a', 64)}"
        };
        var notice = SourceUpdateDetector.Detect(source, observed, Now);
        Assert.NotNull(notice);
        Assert.Equal("pending-review", notice.State);
        Assert.False(notice.MayActivate);
        Assert.True(notice.ContentReviewRequired);
        Assert.Contains("responseSha256", notice.ChangedMetadata);
        Assert.Null(SourceUpdateDetector.Detect(source, source, Now));
        Assert.Throws<AssessmentContractException>(() => SourceUpdateDetector.Detect(source,
            observed with { HttpStatus = 404, RetrievalStatus = "notFound" }, Now));
    }

    [Theory]
    [MemberData(nameof(ControlFixtureCases))]
    public void EveryControlFixtureProducesAnExplicitNonFalsePass(string controlId, string fixtureCase)
    {
        var control = Pack.Controls.Single(item => item.Id == controlId);
        var evidence = ControlEvidenceFixture.Create(control, Pack, fixtureCase);
        var result = new AssessmentEvaluator().Evaluate(Pack, evidence, Now);

        switch (fixtureCase)
        {
            case "matching-candidate":
                Assert.Equal(AssessmentResultState.ManualReview, result.State);
                Assert.All(result.Deltas, delta => Assert.True(delta.Matches));
                break;
            case "mismatching-candidate":
                Assert.Equal(AssessmentResultState.ManualReview, result.State);
                Assert.Contains(result.Deltas, delta => delta.Matches == false);
                break;
            case "manual":
                Assert.Equal(AssessmentResultState.ManualReview, result.State);
                break;
            default:
                Assert.Equal(AssessmentResultState.UnableToAssess, result.State);
                break;
        }

        Assert.Equal(control.Id, result.ControlId);
        Assert.Equal(Pack.PackId, result.BaselinePackId);
        Assert.Equal(Pack.Version, result.BaselinePackVersion);
        Assert.Equal(CanonicalContentHash.Compute(Pack), result.BaselinePackHash);
        Assert.Equal(AssessmentContractVersions.Evaluator, result.EvaluatorVersion);
        Assert.Equal(control.SupportMode, result.SupportMode);
        Assert.False(result.SourceReviewed);
        Assert.False(result.RightsApproved);
        Assert.NotEmpty(result.EvidenceReferences);
        Assert.Equal(evidence.OrganizationId, result.OrganizationId);
        Assert.Equal(evidence.CustomerTenantId, result.CustomerTenantId);
        var expectedCount = control.RequiredInventories.Count == 0
            ? 1
            : evidence.ExpectedInventories.TryGetValue(control.RequiredInventories[0], out var primaryInventory)
                ? primaryInventory.ResourceIds.Count
                : 0;
        Assert.Equal(expectedCount, result.ExpectedCount);
    }

    [Theory]
    [MemberData(nameof(InitialControlIds))]
    public void SyntheticVerifiedHarnessMapsEveryControlToCompliantAndNoncompliantStates(string controlId)
    {
        var control = Pack.Controls.Single(item => item.Id == controlId) with
        {
            ExpectedStateClassification = ExpectedStateClassification.CustomerPolicyCandidate,
            ReviewState = ReviewState.Approved,
            RightsApproved = true,
            SupportMode = "automated/verified",
            OperationQualified = true
        };
        var source = Pack.Sources.Single(item => item.Id == control.SourceId) with { ReviewState = ReviewState.Approved };
        var syntheticPack = Pack with
        {
            Sources = Pack.Sources.Select(item => item.Id == source.Id ? source : item).ToArray(),
            Controls = Pack.Controls.Select(item => item.Id == control.Id ? control : item).ToArray()
        };
        var evaluator = new AssessmentEvaluator();
        var compliant = evaluator.Evaluate(syntheticPack, ControlEvidenceFixture.Create(control, syntheticPack, "matching-candidate"), Now);
        var noncompliant = evaluator.Evaluate(syntheticPack, ControlEvidenceFixture.Create(control, syntheticPack, "mismatching-candidate"), Now);
        Assert.Equal(AssessmentResultState.Compliant, compliant.State);
        Assert.Equal(AssessmentResultState.NonCompliant, noncompliant.State);
    }

    [Theory]
    [MemberData(nameof(FoundationQualifiedFixtureCases))]
    public void FoundationQualifiedHarnessPreservesEveryRequiredOutcomeAndReason(string controlId, string fixtureCase)
    {
        var control = Pack.Controls.Single(item => item.Id == controlId) with
        {
            ExpectedStateClassification = ExpectedStateClassification.CustomerPolicyCandidate,
            ReviewState = ReviewState.Approved,
            RightsApproved = true,
            SupportMode = "automated/verified",
            OperationQualified = true
        };
        var source = Pack.Sources.Single(item => item.Id == control.SourceId) with { ReviewState = ReviewState.Approved };
        var syntheticPack = Pack with
        {
            Sources = Pack.Sources.Select(item => item.Id == source.Id ? source : item).ToArray(),
            Controls = Pack.Controls.Select(item => item.Id == control.Id ? control : item).ToArray()
        };
        var evidence = ControlEvidenceFixture.Create(control, syntheticPack, fixtureCase);
        var result = new AssessmentEvaluator().Evaluate(syntheticPack, evidence, Now);
        var expected = fixtureCase switch
        {
            "matching-candidate" => (AssessmentResultState.Compliant, "All qualified expectations are observed."),
            "mismatching-candidate" => (AssessmentResultState.NonCompliant, "One or more qualified expectations are not observed."),
            "denied" => (AssessmentResultState.UnableToAssess, "Source denied the read operation."),
            "partial" => (AssessmentResultState.UnableToAssess, "Collection was incomplete."),
            "stale" => (AssessmentResultState.UnableToAssess, "Evidence is marked stale."),
            "unsupported" => (AssessmentResultState.UnableToAssess, "The source operation is unsupported."),
            "missing-license" => (AssessmentResultState.UnableToAssess, "A required license is unavailable."),
            "manual" => (AssessmentResultState.ManualReview, "Evidence requires manual review."),
            _ => throw new InvalidOperationException("Unknown foundation fixture case.")
        };
        Assert.Equal(expected.Item1, result.State);
        Assert.Equal(expected.Item2, result.Reason);
        Assert.Equal(controlId, result.ControlId);
        Assert.Equal(evidence.OrganizationId, result.OrganizationId);
        Assert.Equal(evidence.CustomerTenantId, result.CustomerTenantId);
        Assert.Equal(CanonicalContentHash.Compute(syntheticPack), result.BaselinePackHash);
        Assert.Equal(ReviewState.PendingReview, Pack.Controls.Single(item => item.Id == controlId).ReviewState);
        Assert.Equal("manual/unverified", Pack.Controls.Single(item => item.Id == controlId).SupportMode);
    }

    [Theory]
    [InlineData("extra-covered", AssessmentResultState.Compliant)]
    [InlineData("missing-expected", AssessmentResultState.NonCompliant)]
    [InlineData("mismatched-expected", AssessmentResultState.NonCompliant)]
    [InlineData("missing-inventory", AssessmentResultState.UnableToAssess)]
    [InlineData("missing-coverage", AssessmentResultState.UnableToAssess)]
    public void PowerPlatformSubsetRuleUsesTestOnlyQualificationAndRequiresInventoryCoverage(
        string fixtureCase, AssessmentResultState expectedState)
    {
        var control = Pack.Controls.Single(item => item.Id == "PP-001") with
        {
            ExpectedStateClassification = ExpectedStateClassification.CustomerPolicyCandidate,
            ReviewState = ReviewState.Approved,
            RightsApproved = true,
            SupportMode = "automated/verified",
            OperationQualified = true
        };
        var syntheticTestOnlyPack = Pack with
        {
            Sources = Pack.Sources.Select(item => item.Id == control.SourceId
                ? item with { ReviewState = ReviewState.Approved } : item).ToArray(),
            Controls = Pack.Controls.Select(item => item.Id == control.Id ? control : item).ToArray()
        };
        var evidence = ControlEvidenceFixture.Create(control, syntheticTestOnlyPack, "matching-candidate");
        var expectedIds = evidence.ExpectedInventories["environments"].ResourceIds;
        var current = new Dictionary<string, NormalizedValue>(evidence.CurrentState)
        {
            ["expected_environment_ids"] = NormalizedValue.FromStringSet(expectedIds),
            ["covered_environment_ids"] = NormalizedValue.FromStringSet([.. expectedIds, "synthetic-extra-environment"])
        };
        if (fixtureCase == "missing-expected")
        {
            current["covered_environment_ids"] = NormalizedValue.FromStringSet(["synthetic-extra-environment"]);
        }
        if (fixtureCase == "mismatched-expected")
        {
            current["expected_environment_ids"] = NormalizedValue.FromStringSet(["synthetic-extra-environment"]);
        }
        evidence = evidence with { CurrentState = current };
        if (fixtureCase == "missing-inventory")
        {
            evidence = evidence with { ExpectedInventories = new Dictionary<string, ExpectedInventory>() };
        }
        if (fixtureCase == "missing-coverage")
        {
            evidence = evidence with { CoveredInventoryResources = new Dictionary<string, IReadOnlyList<string>>() };
        }

        var result = new AssessmentEvaluator().Evaluate(syntheticTestOnlyPack, evidence, Now);
        Assert.Equal(expectedState, result.State);
        Assert.Equal(ReviewState.PendingReview, Pack.Controls.Single(item => item.Id == control.Id).ReviewState);
        Assert.False(Pack.Controls.Single(item => item.Id == control.Id).OperationQualified);
        Assert.Equal("manual/unverified", Pack.Controls.Single(item => item.Id == control.Id).SupportMode);
    }

    [Fact]
    public void EmptyExpectedInventoryCannotPassAndInventoryMustBeTenantBoundAndApproved()
    {
        var control = Pack.Controls.Single(item => item.Id == "INT-001");
        var baseline = ControlEvidenceFixture.Create(control, Pack, "matching-candidate");
        var inventoryName = control.RequiredInventories[0];
        var empty = baseline with
        {
            ExpectedInventories = new Dictionary<string, ExpectedInventory>(baseline.ExpectedInventories)
            {
                [inventoryName] = baseline.ExpectedInventories[inventoryName] with { ResourceIds = [] }
            }
        };
        var wrongTenant = baseline with
        {
            ExpectedInventories = new Dictionary<string, ExpectedInventory>(baseline.ExpectedInventories)
            {
                [inventoryName] = baseline.ExpectedInventories[inventoryName] with { CustomerTenantId = Guid.NewGuid() }
            }
        };
        var unapproved = baseline with
        {
            ExpectedInventories = new Dictionary<string, ExpectedInventory>(baseline.ExpectedInventories)
            {
                [inventoryName] = baseline.ExpectedInventories[inventoryName] with { Approved = false }
            }
        };
        var evaluator = new AssessmentEvaluator();
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack, empty, Now).State);
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack, wrongTenant, Now).State);
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack, unapproved, Now).State);
    }

    [Fact]
    public void EvidenceMustPinThePackSchemaAndCarryReferencesForCompleteCollection()
    {
        var control = Pack.Controls.Single(item => item.Id == "EXO-001");
        var evidence = ControlEvidenceFixture.Create(control, Pack, "matching-candidate");
        var evaluator = new AssessmentEvaluator();
        var unpinned = evidence with { BaselinePackHash = new string('0', 64) };
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack, unpinned, Now));

        var unknownField = new Dictionary<string, NormalizedValue>(evidence.CurrentState, StringComparer.Ordinal)
        {
            ["undeclared_setting"] = NormalizedValue.FromBoolean(true)
        };
        Assert.Throws<AssessmentContractException>(() => evaluator.Evaluate(Pack,
            evidence with { CurrentState = unknownField }, Now));

        var noReferences = evaluator.Evaluate(Pack, evidence with { EvidenceReferences = [] }, Now);
        Assert.Equal(AssessmentResultState.UnableToAssess, noReferences.State);
    }

    [Fact]
    public void NotApplicableAndExceptionRequireTheirOwnEvidenceAndApproval()
    {
        var control = Pack.Controls.Single(item => item.Id == "PUR-001");
        var baseline = ControlEvidenceFixture.Create(control, Pack, "matching-candidate");
        var evaluator = new AssessmentEvaluator();
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack,
            baseline with { CollectionOutcome = CollectionOutcome.NotApplicable }, Now).State);
        Assert.Equal(AssessmentResultState.NotApplicable, evaluator.Evaluate(Pack,
            baseline with
            {
                CollectionOutcome = CollectionOutcome.NotApplicable,
                Applicability = new ApplicabilityEvidence(baseline.OrganizationId, baseline.CustomerTenantId, control.Id,
                    "scope-record-1", "synthetic-test-scope", Guid.NewGuid(), Now.AddSeconds(-1))
            }, Now).State);
        Assert.Equal(AssessmentResultState.UnableToAssess, evaluator.Evaluate(Pack,
            baseline with { CollectionOutcome = CollectionOutcome.Exception }, Now).State);
        Assert.Equal(AssessmentResultState.Exception, evaluator.Evaluate(Pack,
            baseline with
            {
                CollectionOutcome = CollectionOutcome.Exception,
                Exception = new RiskExceptionEvidence("exception-1", baseline.OrganizationId, baseline.CustomerTenantId,
                    control.Id, "approval-1", Guid.NewGuid(), Now.AddSeconds(-1), Now.AddHours(1))
            }, Now).State);
    }

    [Fact]
    public void ConditionalAccessScopeReportsOverlapsAndFailsClosedOnUnknowns()
    {
        var first = ConditionalAccessPolicy("ca-1");
        var second = ConditionalAccessPolicy("ca-2");
        var overlapping = ConditionalAccessScopeEvaluator.Evaluate("user-1", "app-1", [first, second], true);
        Assert.Equal(ConditionalAccessScopeState.Overlap, overlapping.State);
        Assert.Equal(2, overlapping.MatchingPolicyIds.Count);

        var unknown = ConditionalAccessScopeEvaluator.Evaluate("user-1", "app-1",
            [first, second with { ConditionsSupported = false, UnknownReason = "Named location state is unavailable." }], true);
        Assert.Equal(ConditionalAccessScopeState.Unknown, unknown.State);
        var incomplete = ConditionalAccessScopeEvaluator.Evaluate("user-1", "app-1", [first], false);
        Assert.Equal(ConditionalAccessScopeState.Unknown, incomplete.State);
        var reportOnly = ConditionalAccessScopeEvaluator.Evaluate("user-1", "app-1",
            [first with { State = ConditionalAccessPolicyState.ReportOnly }], true);
        Assert.Equal(ConditionalAccessScopeState.ReportOnly, reportOnly.State);
        Assert.Empty(reportOnly.MatchingPolicyIds);
    }

    [Fact]
    public void ProfileInheritanceRequiresExplicitConflictResolutionAndActivationApproval()
    {
        var layers = new[]
        {
            new BaselineProfileLayer("platform", ProfileLayerKind.Platform, null, null, "pack-a",
                Values("anyone"), "", Guid.Empty, DateTimeOffset.MinValue),
            new BaselineProfileLayer("msp", ProfileLayerKind.Msp, OrganizationId, null, "pack-b",
                Values("organization"), "msp-approval", Guid.NewGuid(), Now),
            new BaselineProfileLayer("customer", ProfileLayerKind.Customer, OrganizationId, CustomerTenantId, "pack-c",
                Values("view"), "customer-approval", Guid.NewGuid(), Now)
        };
        Assert.Throws<AssessmentContractException>(() => BaselineProfileResolver.Resolve(
            "profile", "1.0.0", OrganizationId, CustomerTenantId, layers, []));

        var choice = new ProfileConflictChoice("sharepoint.defaultLink", "msp", "conflict-approval", Guid.NewGuid(), Now);
        var preview = BaselineProfileResolver.Resolve("profile", "1.0.0", OrganizationId, CustomerTenantId, layers, [choice]);
        Assert.Equal("msp", preview.Settings["sharepoint.defaultLink"].OriginLayerId);
        Assert.Equal("conflict-approval", preview.Settings["sharepoint.defaultLink"].ConflictApprovalReference);
        Assert.DoesNotContain("profile-activation", preview.ConflictApprovalReferences);

        var activation = new EffectiveProfileApproval("profile-activation", preview.ContentHash, Guid.NewGuid(), Now,
            OrganizationId, CustomerTenantId);
        var activationCatalog = new EffectiveProfileActivationCatalog();
        Assert.False(activationCatalog.IsActive(preview));
        Assert.Equal(preview.ContentHash, activationCatalog.Activate(preview, activation).ProfileHash);
        Assert.True(activationCatalog.IsActive(preview));
        Assert.Equal(activation, activationCatalog.GetApproval(preview));
        Assert.Throws<AssessmentContractException>(() => activationCatalog.Activate(preview,
            activation with { CustomerTenantId = Guid.NewGuid() }));
        var changedPreview = BaselineProfileResolver.Resolve("profile", "1.0.0", OrganizationId, CustomerTenantId,
            layers, [choice with { SelectedLayerId = "customer", ApprovalReference = "conflict-approval-2" }]);
        Assert.NotEqual(preview.ContentHash, changedPreview.ContentHash);
        Assert.Throws<AssessmentContractException>(() => activationCatalog.Activate(changedPreview,
            activation with { ApprovalReference = "profile-activation-2", ProfileHash = changedPreview.ContentHash }));
    }

    [Fact]
    public void AddAssessmentServicesRegistersEvaluatorAndManualServiceCoverage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssessmentTokenProvider, TestTokenProvider>();
        var packPath = Path.Combine(AppContext.BaseDirectory, "qualified-baseline", "pack-0.1.1.json");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assessment:BaselinePackPath"] = packPath
        }).Build();
        services.AddAssessmentServices(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<AssessmentEvaluator>(provider.GetRequiredService<IAssessmentEvaluator>());
        var candidate = provider.GetRequiredService<IBaselinePackProvider>().LoadConfiguredCandidate();
        Assert.Equal("0.1.1", candidate.Version);
        Assert.False(provider.GetRequiredService<ImmutableBaselineCatalog>()
            .IsActive(candidate.PackId, candidate.Version, candidate.ContentHash));
        var manifests = provider.GetRequiredService<IReadOnlyList<AdapterManifest>>();
        Assert.Equal(13, manifests.Count);
        Assert.All(manifests, manifest =>
        {
            Assert.False(manifest.Enabled);
            Assert.Empty(manifest.Operations);
            Assert.Equal("manual/unverified", manifest.SupportMode);
        });
    }

    [Fact]
    public async Task ReadOnlyAdapterFollowsSameOriginPaginationAndKeepsEvidenceIncompleteAtPageLimit()
    {
        var page = 0;
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal("Bearer test-only-token", request.Headers.Authorization?.ToString());
            page++;
            var body = page == 1
                ? "{\"value\":[{\"id\":\"a\"}],\"@odata.nextLink\":\"http://127.0.0.1/v1.0/items?$skip=next\"}"
                : "{\"value\":[{\"id\":\"b\"}]}";
            return Task.FromResult(JsonResponse(body));
        });
        using var client = new HttpClient(handler);
        var collector = new ReadOnlyAdapterClient(client, new TestTokenProvider(), new AssessmentOptions { MaximumPages = 1 });
        var result = await collector.CollectAsync(TestManifest(), "list-items", CustomerTenantId, "https://graph.microsoft.com/.default", null, CancellationToken.None);
        Assert.Equal(CollectionOutcome.Partial, result.Outcome);
        Assert.False(result.Checkpoint.IsComplete);
        Assert.Equal(1, result.Checkpoint.PagesRetrieved);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ReadOnlyAdapterAcceptsExplicitReadOnlyPostQueryAndRejectsCrossOriginContinuation()
    {
        var postOperation = TestOperation() with
        {
            OperationId = "query-items",
            HttpMethod = "POST",
            RelativePath = "/v1.0/query",
            BodyPolicy = AdapterRequestBodyPolicy.JsonReadQuery,
            ContinuationProperty = null
        };
        var postManifest = TestManifest() with
        {
            AdapterId = "synthetic-post",
            PaginationKind = AdapterPaginationKind.None,
            Operations = [postOperation]
        };
        var postHandler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("{\"query\":\"fixture-only\"}", await request.Content!.ReadAsStringAsync(cancellationToken));
            return JsonResponse("{\"value\":[{\"id\":\"query-result\"}]}");
        });
        using (var postClient = new HttpClient(postHandler))
        {
            var collector = new ReadOnlyAdapterClient(postClient, new TestTokenProvider(), new AssessmentOptions());
            using var query = JsonDocument.Parse("{\"query\":\"fixture-only\"}");
            var postResult = await collector.CollectAsync(postManifest, "query-items", CustomerTenantId,
                "https://graph.microsoft.com/.default", query.RootElement, CancellationToken.None);
            Assert.Equal(CollectionOutcome.Complete, postResult.Outcome);
        }

        var crossOriginHandler = new TestHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            "{\"value\":[],\"@odata.nextLink\":\"https://evil.example/v1.0/items?next=1\"}")));
        using var crossOriginClient = new HttpClient(crossOriginHandler);
        var crossOriginCollector = new ReadOnlyAdapterClient(crossOriginClient, new TestTokenProvider(), new AssessmentOptions());
        await Assert.ThrowsAsync<AssessmentContractException>(() => crossOriginCollector.CollectAsync(
            TestManifest(), "list-items", CustomerTenantId, "https://graph.microsoft.com/.default", null, CancellationToken.None));
        Assert.Equal(1, crossOriginHandler.RequestCount);

        var traversalHandler = new TestHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            "{\"value\":[],\"@odata.nextLink\":\"http://127.0.0.1/v1.0/items/%2e%2e/private\"}")));
        using var traversalClient = new HttpClient(traversalHandler);
        var traversalCollector = new ReadOnlyAdapterClient(traversalClient, new TestTokenProvider(), new AssessmentOptions());
        await Assert.ThrowsAsync<AssessmentContractException>(() => traversalCollector.CollectAsync(
            TestManifest(), "list-items", CustomerTenantId, "https://graph.microsoft.com/.default", null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadOnlyAdapterBoundsRetryAfterAndRejectsNonAllowlistedWrites()
    {
        var attempts = 0;
        var handler = new TestHttpMessageHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("not retained")
                };
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
                return Task.FromResult(throttled);
            }
            return Task.FromResult(JsonResponse("{\"value\":[]}"));
        });
        using var client = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var collector = new ReadOnlyAdapterClient(client, new TestTokenProvider(),
            new AssessmentOptions { MaximumRetryDelaySeconds = 2 }, (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });
        var result = await collector.CollectAsync(TestManifest(),
            "list-items", CustomerTenantId, "https://graph.microsoft.com/.default", null, CancellationToken.None);
        Assert.Equal(CollectionOutcome.Complete, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(delays));

        var write = TestOperation() with { Kind = AdapterOperationKind.Write };
        var invalidManifest = TestManifest() with { Operations = [write] };
        Assert.Throws<AssessmentContractException>(() => AdapterManifestValidator.Validate(invalidManifest));
    }

    private static Guid OrganizationId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static Guid CustomerTenantId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static ConditionalAccessPolicyScope ConditionalAccessPolicy(string id) => new(
        id, ConditionalAccessPolicyState.Enabled, new HashSet<string>(["user-1"], StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(["app-1"], StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal), true, true, null);

    private static Dictionary<string, NormalizedValue> Values(string value) => new(StringComparer.Ordinal)
    {
        ["sharepoint.defaultLink"] = NormalizedValue.FromString(value)
    };

    private static AdapterManifest TestManifest() => new(
        AssessmentContractVersions.AdapterManifest, "synthetic-list", "Synthetic test service", "0.1.0",
        AdapterApiOrigin.SyntheticLoopback, "v1.0", AdapterPaginationKind.ODataNextLink,
        "SyntheticTest", "Test", "manual/unverified", false, true, [], [], [], "Synthetic handler only.", [TestOperation()]);

    private static AdapterOperation TestOperation() => new(
        "list-items", "GET", "/v1.0/items", "/v1.0/", AdapterOperationKind.ReadOnly,
        AdapterRequestBodyPolicy.None, "value", "@odata.nextLink", null, null, false);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class TestTokenProvider : IAssessmentTokenProvider
    {
        public Task<string> GetAccessTokenAsync(Guid customerTenantId, string resourceScope, CancellationToken cancellationToken) =>
            Task.FromResult("test-only-token");
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}

internal static class ControlEvidenceFixture
{
    public static NormalizedEvidence Create(BaselineControl control, BaselinePackDocument pack, string fixtureCase)
    {
        const string organization = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string customerTenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var organizationId = Guid.Parse(organization);
        var customerTenantId = Guid.Parse(customerTenant);
        var inventories = control.RequiredInventories.ToDictionary(
            name => name,
            name => new ExpectedInventory(organizationId, customerTenantId, name, "synthetic-approved-fixture-inventory",
                true, DateTimeOffset.Parse("2026-11-02T10:14:00Z"), [$"fixture-{control.Id}-{name}"]),
            StringComparer.Ordinal);
        var current = BuildMatchingState(control, inventories);
        if (fixtureCase == "mismatching-candidate")
        {
            ApplyMismatch(control.Rules[0], current);
        }

        var coverage = inventories.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ResourceIds.ToArray(), StringComparer.Ordinal);
        var outcome = fixtureCase switch
        {
            "denied" => CollectionOutcome.Denied,
            "partial" => CollectionOutcome.Partial,
            "stale" => CollectionOutcome.Stale,
            "unsupported" => CollectionOutcome.Unsupported,
            "unsupported-cloud" => CollectionOutcome.UnsupportedCloud,
            "missing-license" => CollectionOutcome.MissingLicense,
            "missing-permission" => CollectionOutcome.MissingPermission,
            "throttled" => CollectionOutcome.Throttled,
            "error" => CollectionOutcome.Error,
            "manual" => CollectionOutcome.Manual,
            _ => CollectionOutcome.Complete
        };

        if (fixtureCase == "missing-field")
        {
            current.Remove(control.RequiredFields[0]);
        }
        if (fixtureCase == "missing-inventory" && control.RequiredInventories.Count > 0)
        {
            inventories.Remove(control.RequiredInventories[0]);
            coverage.Remove(control.RequiredInventories[0]);
        }
        else if (fixtureCase == "missing-inventory")
        {
            current.Remove(control.RequiredFields[0]);
        }

        var source = pack.Sources.Single(item => item.Id == control.SourceId);
        return new NormalizedEvidence(AssessmentContractVersions.NormalizedEvidence,
            pack.PackId, pack.Version, CanonicalContentHash.Compute(pack), organizationId, customerTenantId,
            control.Id, control.Service, source.ResolvedUrl,
            source.SourceRevision, source.ResponseSha256, "synthetic-fixture-adapter", "0.1.0",
            DateTimeOffset.Parse("2026-11-02T10:15:00Z"), outcome, current,
            [$"synthetic://fixture/{control.Id}/evidence"], inventories, coverage, null, null);
    }

    private static Dictionary<string, NormalizedValue> BuildMatchingState(
        BaselineControl control,
        IReadOnlyDictionary<string, ExpectedInventory> inventories)
    {
        var values = new Dictionary<string, NormalizedValue>(StringComparer.Ordinal);
        foreach (var rule in control.Rules)
        {
            switch (rule.Operator)
            {
                case RuleOperator.EqualsLiteral:
                    values[rule.Field] = rule.Literal!;
                    break;
                case RuleOperator.EqualsExpectedInventory:
                    values[rule.Field] = NormalizedValue.FromStringSet(inventories[rule.RelatedField!].ResourceIds);
                    break;
                case RuleOperator.EqualsField:
                    values[rule.Field] = values[rule.RelatedField!];
                    break;
                case RuleOperator.IsSubsetOfField:
                    values.TryAdd(rule.Field, NormalizedValue.FromStringSet(["synthetic-emergency"]));
                    values[rule.RelatedField!] = values[rule.Field];
                    break;
                case RuleOperator.IsEmpty:
                    values[rule.Field] = NormalizedValue.FromStringSet([]);
                    break;
                case RuleOperator.IsNonEmpty:
                    values[rule.Field] = NormalizedValue.FromStringSet(["synthetic-policy"]);
                    break;
                case RuleOperator.IsTrue:
                    values[rule.Field] = NormalizedValue.FromBoolean(true);
                    break;
                case RuleOperator.IsFalse:
                    values[rule.Field] = NormalizedValue.FromBoolean(false);
                    break;
                case RuleOperator.IsZero:
                    values[rule.Field] = NormalizedValue.FromNumber(0);
                    break;
            }
        }

        foreach (var field in control.RequiredFields)
        {
            values.TryAdd(field, field.EndsWith("_ids", StringComparison.Ordinal)
                ? NormalizedValue.FromStringSet(["synthetic-resource"])
                : field.EndsWith("_count", StringComparison.Ordinal)
                    ? NormalizedValue.FromNumber(0)
                    : NormalizedValue.FromBoolean(true));
        }
        return values;
    }

    private static void ApplyMismatch(ControlRule rule, IDictionary<string, NormalizedValue> values)
    {
        var current = values[rule.Field];
        values[rule.Field] = rule.Operator switch
        {
            RuleOperator.EqualsLiteral => current.Kind == NormalizedValueKind.String
                ? NormalizedValue.FromString("mismatch")
                : NormalizedValue.FromBoolean(false),
            RuleOperator.EqualsField or RuleOperator.EqualsExpectedInventory => current.Kind == NormalizedValueKind.StringSet
                ? NormalizedValue.FromStringSet(["mismatch"])
                : NormalizedValue.FromString("mismatch"),
            RuleOperator.IsSubsetOfField => NormalizedValue.FromStringSet(["not-in-approved-registry"]),
            RuleOperator.IsEmpty => NormalizedValue.FromStringSet(["unexpected"]),
            RuleOperator.IsNonEmpty => NormalizedValue.FromStringSet([]),
            RuleOperator.IsTrue => NormalizedValue.FromBoolean(false),
            RuleOperator.IsFalse => NormalizedValue.FromBoolean(true),
            RuleOperator.IsZero => NormalizedValue.FromNumber(1),
            _ => current
        };
    }
}