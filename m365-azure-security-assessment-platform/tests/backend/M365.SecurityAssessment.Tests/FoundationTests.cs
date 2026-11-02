using System.Net;
using System.Text.Json;
using M365.SecurityAssessment.Api;
using M365.SecurityAssessment.Domain;

namespace M365.SecurityAssessment.Tests;

public sealed class FoundationTests
{
    private static readonly string InventoryPath = Path.Combine(AppContext.BaseDirectory, "baselines", "control-inventory.json");
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "synthetic-fixtures");

    [Fact]
    public void InventoryHasExactlyThirteenUnverifiedControlsWithoutInventedProvenance()
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        BaselineInventoryValidator.Validate(inventory);
        Assert.Equal(InitialControlIds.All, inventory.Controls.Select(control => control.Id));
        Assert.All(inventory.Controls, control =>
        {
            Assert.Equal("unverified", control.ReviewStatus);
            Assert.Equal("manual/unverified", control.SupportMode);
            Assert.Null(control.SourceRevision);
            Assert.Null(control.ContentHash);
            Assert.StartsWith("https://", control.SourceUrl, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TwoSyntheticFixturesAreCompleteAndTenantDistinct()
    {
        var summaries = LoadCatalog().DescribeFixtures();
        Assert.Equal(2, summaries.Count);
        Assert.Equal(2, summaries.Select(item => item.TenantId).Distinct().Count());
        Assert.All(summaries, item =>
        {
            Assert.Equal("synthetic", item.Source);
            Assert.Equal(ContractVersions.ApiResponse, item.ContractVersion);
            Assert.False(item.LiveConnections);
            Assert.False(item.WritesEnabled);
            Assert.Equal(13, item.ControlCount);
        });
    }

    [Fact]
    public void FixtureAndInventoryVersionMismatchesAreRejected()
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        Assert.Throws<UnsupportedContractVersionException>(() =>
            SyntheticFixtureValidator.Validate(fixture with { SchemaVersion = "2.0" }, inventory));
        Assert.Throws<UnsupportedContractVersionException>(() =>
            BaselineInventoryValidator.Validate(inventory with { SchemaVersion = "2.0" }));
    }

    [Fact]
    public void UnknownContractPropertiesAndUnsafeFixtureFlagsAreRejected()
    {
        Assert.Throws<ContractValidationException>(() => ContractJson.Parse<SyntheticFixtureDocument>("""
            {"schemaVersion":"1.0","source":"synthetic","liveConnections":false,"writesEnabled":false,
             "tenantId":"00000000-0000-0000-0000-000000000001","customerName":"fixture",
             "collectedAtUtc":"2026-01-01T00:00:00Z","controls":[],"unexpected":true}
            """));

        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(fixture with { LiveConnections = true }, inventory));
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(fixture with { WritesEnabled = true }, inventory));
    }

    [Fact]
    public void NumericAndProgrammaticallyUndefinedEnumValuesAreRejected()
    {
        Assert.Throws<ContractValidationException>(() => ContractJson.Parse<SyntheticControlFixture>("""
            {"controlId":"EXO-001","collectionState":99}
            """));

        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        var invalidControl = fixture.Controls[0] with { CollectionState = (CollectionState)99 };
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(
            fixture with { Controls = [invalidControl, .. fixture.Controls.Skip(1)] }, inventory));
        Assert.Throws<ContractValidationException>(() => SyntheticControlEvaluator.Evaluate(
            inventory.Controls[0], invalidControl, fixture.TenantId, fixture.CollectedAtUtc));

        var audit = new SettingAuditEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "service", "resource", "setting", new AuditValue((AuditValueKind)99, null, AuditValueSensitivity.ClassifiedUnknown),
            AuditValue.Unknown(), AuditValue.Unknown(), "blocked", Guid.NewGuid());
        Assert.Throws<ContractValidationException>(() => SettingAuditEventValidator.Validate(audit));
    }

    [Fact]
    public void VersionedContractsRejectUnknownVersionsAtJsonIngress()
    {
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<EvidenceRecord>("""
            {"contractVersion":"2.0","tenantId":"00000000-0000-0000-0000-000000000001",
             "service":"service","resourceId":"resource","collectedAtUtc":"2026-01-01T00:00:00Z",
             "source":"synthetic","supportMode":"manual/unverified","adapterVersion":"1","integrityReference":"fixture"}
            """));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<AdapterManifest>("""
            {"contractVersion":"2.0","adapterId":"adapter","service":"service","supportMode":"manual/unverified",
             "readOperations":[],"writeOperations":[],"runtime":"synthetic"}
            """));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<OperationContract>("""
            {"contractVersion":"2.0","operationId":"operation","service":"service","setting":"setting",
             "mode":"guidanceOnly","limitation":"unverified"}
            """));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractVersionValidator.ValidateIngress(
            new SyntheticFixtureSummary("2.0", "synthetic", false, false, Guid.NewGuid(), "fixture", 13)));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractVersionValidator.ValidateIngress(
            new SyntheticAssessmentResponse("2.0", "synthetic", false, false, "synthetic-fixture",
                Guid.NewGuid(), "fixture", DateTimeOffset.UtcNow, [])));
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("adapter")]
    [InlineData("operation")]
    [InlineData("job")]
    [InlineData("summary")]
    [InlineData("assessment")]
    [InlineData("inventory")]
    [InlineData("fixture")]
    public void VersionedContractsValidateSerializedObjectsArraysListsAndNestedCollections(string kind)
    {
        switch (kind)
        {
            case "evidence":
                VerifyVersionedRoundTrips(new EvidenceRecord("1.0", Guid.NewGuid(), "service", "resource",
                    DateTimeOffset.UtcNow, "synthetic", "manual/unverified", "1", "fixture"));
                break;
            case "adapter":
                VerifyVersionedRoundTrips(new AdapterManifest("1.0", "adapter", "service", "manual/unverified", [], [], "synthetic"));
                break;
            case "operation":
                VerifyVersionedRoundTrips(new OperationContract("1.0", "operation", "service", "setting", OperationMode.GuidanceOnly, "unverified"));
                break;
            case "job":
                VerifyVersionedRoundTrips(NewEnvelope(NewJob(JobPurpose.Assessment, false)));
                break;
            case "summary":
                VerifyVersionedRoundTrips(LoadCatalog().DescribeFixtures()[0]);
                break;
            case "assessment":
                VerifyVersionedRoundTrips(LoadCatalog().Evaluate(Guid.Parse("10000000-0000-0000-0000-000000000001"))!);
                break;
            case "inventory":
                VerifyVersionedRoundTrips(ContractJson.Load<BaselineInventoryDocument>(InventoryPath), "schemaVersion");
                break;
            case "fixture":
                VerifyVersionedRoundTrips(ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json")), "schemaVersion");
                break;
        }
    }

    private static void VerifyVersionedRoundTrips<T>(T contract, string versionField = "contractVersion") where T : class
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        var validJson = JsonSerializer.Serialize(contract, options);
        var invalidDocument = System.Text.Json.Nodes.JsonNode.Parse(validJson)!;
        invalidDocument[versionField] = "2.0";
        var invalidJson = invalidDocument.ToJsonString();

        Assert.NotNull(ContractJson.Parse<T>(validJson));
        Assert.Equal(2, ContractJson.Parse<T[]>($"[{validJson},{validJson}]").Length);
        var listJson = JsonSerializer.Serialize(new List<T> { contract, contract }, options);
        Assert.Equal(2, ContractJson.Parse<List<T>>(listJson).Count);
        Assert.Single(ContractJson.Parse<List<T[]>>($"[[{validJson}]]"));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<T>(invalidJson));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<T[]>($"[{validJson},{invalidJson}]"));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<List<T>>($"[{validJson},{invalidJson}]"));
        Assert.Throws<UnsupportedContractVersionException>(() => ContractJson.Parse<List<T[]>>($"[[{validJson}],[{invalidJson}]]"));
    }

    [Theory]
    [InlineData(CollectionState.Denied, AssessmentStatus.UnableToAssess, EvaluationReason.CollectionDenied)]
    [InlineData(CollectionState.Partial, AssessmentStatus.UnableToAssess, EvaluationReason.CollectionPartial)]
    [InlineData(CollectionState.Stale, AssessmentStatus.UnableToAssess, EvaluationReason.EvidenceStale)]
    [InlineData(CollectionState.Unsupported, AssessmentStatus.UnableToAssess, EvaluationReason.ServiceUnsupported)]
    [InlineData(CollectionState.MissingLicense, AssessmentStatus.UnableToAssess, EvaluationReason.LicenseUnavailable)]
    [InlineData(CollectionState.Manual, AssessmentStatus.ManualReview, EvaluationReason.ManualEvidenceRequired)]
    public void EvaluationPreservesDistinctUnavailableAndManualReasons(
        CollectionState collectionState, AssessmentStatus status, EvaluationReason reason)
    {
        var definition = ContractJson.Load<BaselineInventoryDocument>(InventoryPath).Controls[0];
        var fixtureControl = new SyntheticControlFixture { ControlId = definition.Id, CollectionState = collectionState };
        var result = SyntheticControlEvaluator.Evaluate(definition, fixtureControl, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(status, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Equal("manual/unverified", result.SupportMode);
        Assert.Null(result.SourceRevision);
    }

    [Fact]
    public void CompleteSyntheticFixtureDoesNotClaimUnverifiedControlsAreCompliant()
    {
        var catalog = LoadCatalog();
        var compliant = catalog.Evaluate(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var noncompliant = catalog.Evaluate(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        Assert.NotNull(compliant);
        Assert.NotNull(noncompliant);
        Assert.All(compliant.Controls, item =>
        {
            Assert.Equal(AssessmentStatus.ManualReview, item.Status);
            Assert.Equal(EvaluationReason.ManualEvidenceRequired, item.Reason);
            Assert.True(item.SimulationMatchesExpected);
        });
        Assert.Equal(AssessmentStatus.ManualReview, noncompliant.Controls.Single(item => item.ControlId == "EXO-001").Status);
        Assert.Equal("synthetic", noncompliant.Source);
        Assert.False(noncompliant.LiveConnections);
        Assert.False(noncompliant.WritesEnabled);
        Assert.Equal("manual/unverified", noncompliant.Controls[0].SupportMode);
        Assert.Equal(ContractVersions.ApiResponse, noncompliant.ContractVersion);
    }

    [Fact]
    public void CustomerBUsesTenantScopedExpectedIdsWithoutInventingExtraFailures()
    {
        var result = LoadCatalog().Evaluate(Guid.Parse("20000000-0000-0000-0000-000000000002"));

        Assert.NotNull(result);
        Assert.All(result.Controls.Where(control => control.ControlId != "EXO-001"),
            control => Assert.True(control.SimulationMatchesExpected, control.ControlId));
        Assert.False(result.Controls.Single(control => control.ControlId == "EXO-001").SimulationMatchesExpected);
        Assert.All(result.Controls, control => Assert.Equal(AssessmentStatus.ManualReview, control.Status));
        Assert.Contains("role-ga-b", result.Controls.Single(control => control.ControlId == "ENT-001")
            .ExpectedState["privileged_role_ids"].EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void ExpectedCoverageUsesRequiredInventoryRatherThanFailingObservedCoverage()
    {
        var definition = ContractJson.Load<BaselineInventoryDocument>(InventoryPath).Controls.Single(control => control.Id == "MDE-001");
        var observed = definition.ExpectedState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        observed["expected_device_ids"] = JsonSerializer.SerializeToElement(new[] { "device-1", "device-2" });
        observed["tamper_protected_device_ids"] = JsonSerializer.SerializeToElement(new[] { "device-1" });
        var evidence = new SyntheticControlFixture
        {
            ControlId = definition.Id, CollectionState = CollectionState.Complete,
            ResourceIds = ["device-1", "device-2"], ObservedState = observed
        };

        var result = SyntheticControlEvaluator.Evaluate(definition, evidence, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.False(result.SimulationMatchesExpected);
        Assert.Equal(new[] { "device-1", "device-2" }, result.ExpectedState["tamper_protected_device_ids"].EnumerateArray().Select(value => value.GetString()));
        Assert.Single(result.ObservedState["tamper_protected_device_ids"].EnumerateArray());
        Assert.Equal(AssessmentStatus.ManualReview, result.Status);
        Assert.Null(result.SourceRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpectedSubsetRetainsAllowedChoicesButNeverForbiddenObservations(bool forbidden)
    {
        var definition = ContractJson.Load<BaselineInventoryDocument>(InventoryPath).Controls.Single(control => control.Id == "ENT-001");
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        var evidence = fixture.Controls.Single(control => control.ControlId == definition.Id);
        var observed = evidence.ObservedState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        observed["emergency_registry_ids"] = JsonSerializer.SerializeToElement(new[] { "emergency-1", "emergency-2" });
        observed["excluded_principal_ids"] = JsonSerializer.SerializeToElement(forbidden
            ? new[] { "emergency-1", "not-approved" } : new[] { "emergency-1" });

        var result = SyntheticControlEvaluator.Evaluate(definition, evidence with { ObservedState = observed }, fixture.TenantId, fixture.CollectedAtUtc);

        Assert.Equal(!forbidden, result.SimulationMatchesExpected);
        Assert.Equal(new[] { "emergency-1" }, result.ExpectedState["excluded_principal_ids"].EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(AssessmentStatus.ManualReview, result.Status);
    }

    [Theory]
    [InlineData("ENT-001")]
    [InlineData("EXO-001")]
    [InlineData("TEAM-001")]
    [InlineData("SPO-001")]
    [InlineData("PP-001")]
    [InlineData("INT-001")]
    [InlineData("MDI-001")]
    [InlineData("MDO-001")]
    [InlineData("MDE-001")]
    [InlineData("MDCA-001")]
    [InlineData("XDR-001")]
    [InlineData("PUR-001")]
    [InlineData("AZR-001")]
    public void EachCompleteControlFixtureReportsSimulationMatchWithoutClaimingCompliance(string controlId)
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        var definition = inventory.Controls.Single(control => control.Id == controlId);
        var evidence = fixture.Controls.Single(control => control.ControlId == controlId);

        var result = SyntheticControlEvaluator.Evaluate(definition, evidence, fixture.TenantId, fixture.CollectedAtUtc);

        Assert.Equal(AssessmentStatus.ManualReview, result.Status);
        Assert.Equal(EvaluationReason.ManualEvidenceRequired, result.Reason);
        Assert.True(result.SimulationMatchesExpected);
    }

    [Fact]
    public void MissingExpectedEvidenceCannotPass()
    {
        var definition = ContractJson.Load<BaselineInventoryDocument>(InventoryPath).Controls[0];
        var missingField = definition.ExpectedState.Keys.First();
        var observed = definition.ExpectedState.Where(pair => pair.Key != missingField)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var result = SyntheticControlEvaluator.Evaluate(definition,
            new SyntheticControlFixture { ControlId = definition.Id, CollectionState = CollectionState.Complete, ObservedState = observed },
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(AssessmentStatus.UnableToAssess, result.Status);
        Assert.Equal(EvaluationReason.RequiredEvidenceMissing, result.Reason);
    }

    [Fact]
    public void FixtureRejectsMissingOrDuplicatedControlIDs()
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        var invalid = fixture with { Controls = fixture.Controls.Skip(1).ToArray() };
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(invalid, inventory));
    }

    [Fact]
    public void InventoryAndFixtureValidationProtectTheThirteenControlDenominatorAndScopedResources()
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-b.json"));
        Assert.Equal(InitialControlIds.All.Length, inventory.Controls.Count);
        Assert.Equal(InitialControlIds.All.Length, fixture.Controls.Count);
        SyntheticFixtureValidator.Validate(fixture, inventory);

        var missingInventoryControl = inventory with { Controls = inventory.Controls.Skip(1).ToArray() };
        Assert.Throws<ContractValidationException>(() => BaselineInventoryValidator.Validate(missingInventoryControl));

        var intune = fixture.Controls.Single(control => control.ControlId == "INT-001");
        var mismatchedResources = intune with { ResourceIds = ["device-from-another-scope"] };
        var invalidFixture = fixture with
        {
            Controls = fixture.Controls.Select(control => control.ControlId == "INT-001" ? mismatchedResources : control).ToArray()
        };
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(invalidFixture, inventory));

        using var invalidSet = JsonDocument.Parse("[1]");
        var observed = intune.ObservedState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        observed["expected_device_ids"] = invalidSet.RootElement.Clone();
        var malformedSet = intune with { ObservedState = observed };
        invalidFixture = fixture with
        {
            Controls = fixture.Controls.Select(control => control.ControlId == "INT-001" ? malformedSet : control).ToArray()
        };
        Assert.Throws<ContractValidationException>(() => SyntheticFixtureValidator.Validate(invalidFixture, inventory));
    }

    [Fact]
    public void OnlyManifestDeclaredSetFieldsIgnoreArrayOrder()
    {
        var inventory = ContractJson.Load<BaselineInventoryDocument>(InventoryPath);
        var fixture = ContractJson.Load<SyntheticFixtureDocument>(Path.Combine(FixtureDirectory, "customer-a.json"));
        var definition = inventory.Controls.Single(control => control.Id == "ENT-001") with
        {
            ReviewStatus = "verified",
            SupportMode = "verified"
        };
        var evidence = fixture.Controls.Single(control => control.ControlId == "ENT-001");
        using var reversedSet = JsonDocument.Parse("[\"role-pra\",\"role-ga\"]");
        var observed = evidence.ObservedState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        observed["privileged_role_ids"] = reversedSet.RootElement.Clone();
        observed["covered_role_ids"] = reversedSet.RootElement.Clone();
        var reorderedEvidence = evidence with { ObservedState = observed };

        var declaredSetResult = SyntheticControlEvaluator.Evaluate(definition, reorderedEvidence,
            fixture.TenantId, fixture.CollectedAtUtc);
        Assert.Equal(AssessmentStatus.Compliant, declaredSetResult.Status);

        var orderedDefinition = definition with { SetFields = [], SetEqualityFields = [], SetSubsetFields = [] };
        var orderedResult = SyntheticControlEvaluator.Evaluate(orderedDefinition, reorderedEvidence,
            fixture.TenantId, fixture.CollectedAtUtc);
        Assert.Equal(AssessmentStatus.NonCompliant, orderedResult.Status);
    }

    [Fact]
    public void JobAdmissionRejectsTenantAndPurposeMismatches()
    {
        var job = NewJob(JobPurpose.Assessment, false);
        var envelope = NewEnvelope(job);
        var connection = NewConnection(job);
        var tenantMismatch = JobAdmission.Admit(job, envelope,
            new WorkerBinding(job.OrganizationId, Guid.NewGuid(), job.Purpose), connection, false);
        var purposeMismatch = JobAdmission.Admit(job, envelope,
            new WorkerBinding(job.OrganizationId, job.TenantId, JobPurpose.Remediation), connection, false);
        Assert.Equal("tenant-binding-mismatch", tenantMismatch.ReasonCode);
        Assert.Equal("purpose-binding-mismatch", purposeMismatch.ReasonCode);

        var admitted = JobAdmission.Admit(job, envelope,
            NewWorker(job), connection, writesEnabled: false);
        Assert.True(admitted.Admitted);
    }

    [Fact]
    public void JobAdmissionRejectsOrganizationAndCredentialBindingMismatches()
    {
        var job = NewJob(JobPurpose.Assessment, false);
        var envelope = NewEnvelope(job);
        var worker = NewWorker(job);
        var connection = NewConnection(job);

        Assert.Equal("organization-binding-mismatch", JobAdmission.Admit(job, envelope,
            worker with { OrganizationId = Guid.NewGuid() }, connection, false).ReasonCode);
        Assert.Equal("organization-binding-mismatch", JobAdmission.Admit(job, envelope,
            worker, connection with { OrganizationId = Guid.NewGuid() }, false).ReasonCode);
        Assert.Equal("credential-reference-mismatch", JobAdmission.Admit(job, envelope,
            worker, connection with { CredentialReferenceId = "credential-other" }, false).ReasonCode);
        Assert.Equal("credential-purpose-mismatch", JobAdmission.Admit(job, envelope,
            worker, connection with { CredentialPurpose = JobPurpose.Remediation }, false).ReasonCode);
        Assert.Equal("incomplete-binding", JobAdmission.Admit(job with { CredentialReferenceId = " " }, envelope,
            worker, connection, false).ReasonCode);
    }

    [Fact]
    public void JobAdmissionRejectsQueueVersionOwnershipMismatchAndWrites()
    {
        var job = NewJob(JobPurpose.Remediation, true);
        var worker = NewWorker(job);
        var connection = NewConnection(job);
        Assert.Equal("unsupported-contract-version", JobAdmission.Admit(
            job, NewEnvelope(job) with { ContractVersion = "2.0" }, worker, connection, false).ReasonCode);
        Assert.Equal("queue-envelope-mismatch", JobAdmission.Admit(
            job, NewEnvelope(job) with { TenantId = Guid.NewGuid() }, worker, connection, false).ReasonCode);
        Assert.Equal("writes-disabled", JobAdmission.Admit(job, NewEnvelope(job), worker, connection, false).ReasonCode);
    }

    [Fact]
    public void AuditContractUsesImmutableActorAndRedactsSensitiveValues()
    {
        var actorId = Guid.NewGuid();
        var audit = new SettingAuditEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), actorId,
            "Exchange Online", "resource-1", "clientSecret",
            AuditValueRedactor.Redact("clientSecret", AuditValue.Known("previous", AuditValueSensitivity.NonSensitive)),
            AuditValueRedactor.Redact("clientSecret", AuditValue.Known("new-secret", AuditValueSensitivity.Sensitive)),
            AuditValue.Unknown(), "unknown", Guid.NewGuid());
        SettingAuditEventValidator.Validate(audit);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact(audit.SettingName, audit.RequestedNewValue).Kind);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact("certificate",
            AuditValue.Known("-----BEGIN PRIVATE KEY-----\nmaterial", AuditValueSensitivity.NonSensitive)).Kind);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact("api_key",
            AuditValue.Known("ordinary-looking-value", AuditValueSensitivity.NonSensitive)).Kind);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact("setting",
            AuditValue.Known("unclassified-value", AuditValueSensitivity.ClassifiedUnknown)).Kind);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact("setting",
            AuditValue.Known("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ1c2VyMTIzNCJ9.signaturetokenvalue", AuditValueSensitivity.NonSensitive)).Kind);
        Assert.Equal(AuditValueKind.Redacted, AuditValueRedactor.Redact("setting",
            AuditValue.Known("aB3dE5fG7hJ9kL2mN4pQ6rT8vW0xY1zA3cD5eF7g", AuditValueSensitivity.NonSensitive)).Kind);
        var nonUtc = new SettingAuditEvent(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(1)),
            Guid.NewGuid(), Guid.NewGuid(), actorId, "service", "resource", "setting",
            AuditValue.Absent(), AuditValue.Unknown(), AuditValue.Unknown(), "blocked", Guid.NewGuid());
        Assert.Throws<ContractValidationException>(() => SettingAuditEventValidator.Validate(nonUtc));
    }

    [Fact]
    public void SyntheticPreviewRequiresDevelopmentOptInAndLoopback()
    {
        Assert.Throws<InvalidOperationException>(() => SyntheticPreviewPolicy.ValidateConfiguration("Production", true));
        Assert.False(SyntheticPreviewPolicy.IsEnabled("Development", false));
        Assert.True(SyntheticPreviewPolicy.IsEnabled("Development", true));
        Assert.True(SyntheticPreviewPolicy.IsLoopback(IPAddress.Loopback));
        Assert.True(SyntheticPreviewPolicy.IsLoopback(IPAddress.IPv6Loopback));
        Assert.False(SyntheticPreviewPolicy.IsLoopback(IPAddress.Parse("192.0.2.10")));
        Assert.False(SyntheticPreviewPolicy.IsLoopback(null));
    }

    [Fact]
    public void PublicResponseContractsAreVersionedAndDoNotExposeCredentialFields()
    {
        var safeContracts = new[]
        {
            typeof(SyntheticAssessmentResponse), typeof(SyntheticFixtureSummary), typeof(SyntheticControlEvaluation),
            typeof(EvidenceRecord), typeof(AdapterManifest), typeof(OperationContract), typeof(JobEnvelope)
        };
        var forbiddenPropertyNames = new[] { "credential", "password", "secret", "token" };

        Assert.All(safeContracts, contract =>
        {
            var propertyNames = contract.GetProperties().Select(property => property.Name).ToArray();
            Assert.DoesNotContain(propertyNames, name => forbiddenPropertyNames.Any(forbidden =>
                name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
        });

        var adapter = new AdapterManifest(ContractVersions.AdapterManifest, "synthetic-fixtures",
            "all-initial-controls", "manual/unverified", [], [], "in-memory-only");
        var operation = new OperationContract(ContractVersions.Operation, "synthetic-preview-only",
            "none", "none", OperationMode.GuidanceOnly, "No customer service operation is implemented.");
        Assert.Equal(ContractVersions.AdapterManifest, adapter.ContractVersion);
        Assert.Empty(adapter.ReadOperations);
        Assert.Empty(adapter.WriteOperations);
        Assert.Equal(ContractVersions.Operation, operation.ContractVersion);
        Assert.NotEqual(OperationMode.Enabled, operation.Mode);
    }

    private static SyntheticFixtureCatalog LoadCatalog() => SyntheticFixtureCatalog.Load(InventoryPath, FixtureDirectory);
    private static StoredJob NewJob(JobPurpose purpose, bool write) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), purpose, "connection-a", write, "credential-a");
    private static JobEnvelope NewEnvelope(StoredJob job) =>
        new(ContractVersions.JobEnvelope, job.JobId, job.OrganizationId, job.TenantId, job.Purpose, job.ConnectionId);
    private static WorkerBinding NewWorker(StoredJob job) =>
        new(job.OrganizationId, job.TenantId, job.Purpose);
    private static ConnectionBinding NewConnection(StoredJob job) =>
        new(job.ConnectionId, job.OrganizationId, job.TenantId, job.Purpose,
            job.CredentialReferenceId, job.Purpose, true);
}