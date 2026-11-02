from infra.validation.scan_nuget_vulnerabilities import find_vulnerabilities
from infra.validation.write_release_artifacts import create_artifacts
from infra.validation.validate_deployment import validate_manifest, validate_release_manifest
import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))


VALID_DIGEST = "sha256:" + "a" * 64


def base_manifest() -> dict:
    return {
        "environmentClass": "development-test",
        "deploymentMode": "central",
        "location": "eastus2",
        "networkPlan": {
            "vnetCidr": "10.20.0.0/16",
            "webAca": "10.20.0.0/23",
            "apiAca": "10.20.2.0/23",
            "postgres": "10.20.4.0/28",
            "privateEndpoints": "10.20.5.0/27",
        },
        "customerWorkerBindings": [{
            "tenantId": "10000000-0000-0000-0000-000000000001",
            "assessmentManifestDigest": "",
            "assessmentManifestApproval": {},
            "remediationManifestDigest": "",
            "remediationManifestApproval": {},
        }],
        "windowsWorkerManifestDigests": [],
        "serviceBusSku": "Standard",
        "images": {
            name: f"ghcr.io/example/{name}@{VALID_DIGEST}"
            for name in ("web", "api", "assessment", "remediation")
        },
        "privateAccess": {
            "postgres": True,
            "blob": True,
            "keyVault": True,
            "serviceBus": True,
            "apiIngressPublic": False,
            "separateWebPerimeter": True,
        },
        "postgresAppRole": {
            "name": "platform_app",
            "superuser": False,
            "createDatabase": False,
            "createRole": False,
            "bypassRls": False,
            "rowLevelSecurity": True,
            "forceRowLevelSecurity": True,
        },
        "identities": {
            "web": {"roles": [], "credentialPurpose": None, "credentialTenantIds": [], "verifiedManifestDigests": []},
            "api": {
                "roles": ["servicebus-send-assessment", "servicebus-send-remediation", "keyvault-crypto"],
                "credentialPurpose": None,
                "credentialTenantIds": [],
                "verifiedManifestDigests": [],
            },
            "assessment": {
                "roles": [],
                "credentialPurpose": "assessment",
                "credentialTenantIds": [],
                "verifiedManifestDigests": [],
            },
            "remediation": {
                "roles": [],
                "credentialPurpose": "remediation",
                "credentialTenantIds": [],
                "verifiedManifestDigests": [],
            },
        },
        "approvals": {},
    }


def production_approvals() -> dict:
    approval = {"approved": True, "reference": "approval-ref-1",
                "approvedBy": "operator-1", "approvedAtUtc": "2026-11-02T12:00:00Z"}
    return {
        "region": {**approval, "approvedRegion": "eastus2"},
        "networkProfile": {**approval, "profileId": "network-prod-1", "egressPolicyReference": "egress-policy-1", "firewallPrivateIp": "10.20.0.4", "approvedMicrosoftApiFqdns": ["login.microsoftonline.com", "graph.microsoft.com", "management.azure.com"]},
        "premiumCapacityCost": {**approval, "sku": "Premium", "messagingUnits": 1, "workloadProfileId": "workload-profile-1", "estimateSource": "Azure Pricing Calculator review", "estimatedMonthlyCost": "250.00", "currency": "USD"},
        "budget": {**approval, "monthlyLimit": 1000, "currency": "USD"},
        "retention": {**approval, "evidenceDays": 90, "auditDays": 365, "backupDays": 35, "postgresGeoRedundantBackup": "Disabled"},
        "productionSlo": {
            **approval,
            "availabilityPercent": 99.9,
            "queueAgeAlertSeconds": 600,
            "jobTimeoutSeconds": 1800,
            "perCustomerConcurrency": 2,
            "maxTransientRetries": 5,
            "recoveryRtoHours": 4,
            "recoveryRpoMinutes": 15,
        },
    }


class DeploymentValidationTests(unittest.TestCase):
    def test_development_profile_is_explicitly_nonproduction(self) -> None:
        defaults = json.loads(
            (ROOT / "infra/validation/development-test-defaults.json").read_text(encoding="utf-8"))
        self.assertEqual("development-test", defaults["profile"])
        self.assertIs(defaults["productionSlo"], False)
        self.assertEqual(2, defaults["perCustomerActiveJobs"])
        self.assertEqual(1800, defaults["jobTimeoutSeconds"])
        self.assertEqual(5, defaults["maxTransientRetries"])
        self.assertEqual(900, defaults["maxRetryBackoffSeconds"])
        self.assertIs(defaults["honorRetryAfter"], True)
        self.assertEqual(600, defaults["queueAgeAlertSeconds"])
        self.assertEqual(600, defaults["uncertainWriteReconcileSeconds"])
        self.assertEqual("read-before-retry",
                         defaults["uncertainWriteRetryPolicy"])
        self.assertEqual(14, defaults["syntheticFixtureRetentionDays"])
        self.assertEqual([75, 90], defaults["budgetAlertPercent"])
        self.assertEqual(4, defaults["recoveryRtoHours"])
        self.assertEqual(15, defaults["recoveryRpoMinutes"])
        self.assertEqual([], validate_manifest(base_manifest()))

    def test_production_without_explicit_approvals_is_rejected(self) -> None:
        manifest = base_manifest()
        manifest["environmentClass"] = "production"
        errors = validate_manifest(manifest)
        self.assertTrue(
            any("Service Bus Premium" in error for error in errors))
        self.assertTrue(any("region" in error for error in errors))
        self.assertTrue(any("productionSlo" in error for error in errors))

    def test_checked_in_production_template_fails_until_approvals_are_filled(self) -> None:
        template = json.loads(
            (ROOT / "infra/azure/production-approval.template.json").read_text(encoding="utf-8"))
        errors = validate_manifest(template)
        self.assertTrue(any("networkProfile" in error for error in errors))
        self.assertTrue(any("productionSlo" in error for error in errors))
        self.assertTrue(any("Premium" in error for error in errors))

    def test_production_requires_approved_region_and_premium_capacity(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        self.assertEqual([], validate_manifest(manifest))
        manifest["serviceBusSku"] = "Standard"
        self.assertTrue(
            any("Service Bus Premium" in error for error in validate_manifest(manifest)))

    def test_production_approval_region_must_match_selected_location(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        manifest["approvals"]["region"]["approvedRegion"] = "westus3"
        self.assertTrue(
            any("match location" in error for error in validate_manifest(manifest)))

    def test_release_images_require_immutable_digests(self) -> None:
        manifest = base_manifest()
        manifest["images"]["api"] = "ghcr.io/example/api:release"
        self.assertTrue(
            any("images.api" in error for error in validate_manifest(manifest)))

    def test_private_service_access_and_internal_api_are_required(self) -> None:
        manifest = base_manifest()
        manifest["privateAccess"]["apiIngressPublic"] = True
        manifest["privateAccess"]["postgres"] = False
        errors = validate_manifest(manifest)
        self.assertTrue(any("apiIngressPublic" in error for error in errors))
        self.assertTrue(
            any("privateAccess.postgres" in error for error in errors))

    def test_app_role_cannot_be_database_admin_or_bypass_rls(self) -> None:
        manifest = base_manifest()
        manifest["postgresAppRole"]["bypassRls"] = True
        self.assertTrue(
            any("bypassRls" in error for error in validate_manifest(manifest)))

    def test_connector_credential_scopes_are_customer_and_purpose_bound(self) -> None:
        manifest = base_manifest()
        tenant_id = "10000000-0000-0000-0000-000000000001"
        manifest["customerWorkerBindings"][0].update(
            assessmentManifestDigest=VALID_DIGEST,
            assessmentManifestApproval={
                "approved": True,
                "reference": "assessment-manifest-review-1",
                "approvedBy": "reviewer-1",
                "approvedAtUtc": "2026-11-02T12:00:00Z",
            },
        )
        manifest["identities"]["assessment"]["roles"] = [
            "servicebus-receive-assessment", "blob-write-evidence"]
        manifest["identities"]["assessment"]["credentialTenantIds"] = [tenant_id]
        manifest["identities"]["assessment"]["verifiedManifestDigests"] = [
            VALID_DIGEST]
        self.assertEqual([], validate_manifest(manifest))
        manifest["identities"]["assessment"]["credentialPurpose"] = "remediation"
        manifest["identities"]["api"]["roles"].append("Owner")
        errors = validate_manifest(manifest)
        self.assertTrue(
            any("Purpose must be assessment" in error for error in errors))
        self.assertTrue(any("overbroad grant" in error for error in errors))

    def test_web_and_api_cannot_receive_customer_credentials(self) -> None:
        manifest = base_manifest()
        manifest["identities"]["api"]["credentialTenantIds"] = [
            "10000000-0000-0000-0000-000000000001"]
        self.assertTrue(any(
            "must not receive customer connector credentials" in error for error in validate_manifest(manifest)))

    def test_worker_bindings_default_to_zero_and_require_an_approved_manifest_digest(self) -> None:
        manifest = base_manifest()
        self.assertEqual([], validate_manifest(manifest))
        self.assertEqual([], manifest["identities"]["assessment"]["roles"])
        self.assertEqual([], manifest["identities"]["remediation"]["roles"])
        manifest["customerWorkerBindings"][0]["assessmentManifestDigest"] = "sha256:mutable-tag"
        self.assertTrue(any(
            "assessmentManifestDigest must be an immutable SHA-256 digest" in error for error in validate_manifest(manifest)))

    def test_windows_worker_manifests_remain_blocked_without_an_isolated_host(self) -> None:
        manifest = base_manifest()
        manifest["windowsWorkerManifestDigests"] = [VALID_DIGEST]
        self.assertTrue(any(
            "no separately approved isolated Windows VM host" in error for error in validate_manifest(manifest)))

    def test_production_estimate_must_include_workload_profile_and_budget(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        manifest["approvals"]["premiumCapacityCost"]["estimatedMonthlyCost"] = 0
        manifest["approvals"]["budget"]["monthlyLimit"] = 0
        errors = validate_manifest(manifest)
        self.assertTrue(any("monthly estimate" in error for error in errors))
        self.assertTrue(any("monthlyLimit" in error for error in errors))

    def test_approval_timestamps_must_be_utc(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        manifest["approvals"]["budget"]["approvedAtUtc"] = "2026-11-02T12:00:00+02:00"
        self.assertTrue(
            any("must be UTC" in error for error in validate_manifest(manifest)))

    def test_manifest_rejects_embedded_secret_values(self) -> None:
        manifest = copy.deepcopy(base_manifest())
        manifest["connectorPassword"] = "not-a-real-password"
        self.assertTrue(
            any("credential material" in error for error in validate_manifest(manifest)))

    def test_preproduction_requires_an_approved_private_egress_profile(self) -> None:
        manifest = base_manifest()
        manifest["environmentClass"] = "pre-production"
        self.assertTrue(
            any("networkProfile" in error for error in validate_manifest(manifest)))
        manifest["approvals"]["networkProfile"] = {
            "approved": True,
            "reference": "network-approval-1",
            "approvedBy": "operator-1",
            "approvedAtUtc": "2026-11-02T12:00:00Z",
            "profileId": "preproduction-network-1",
            "egressPolicyReference": "egress-policy-1",
            "firewallPrivateIp": "10.20.0.4",
            "approvedMicrosoftApiFqdns": ["login.microsoftonline.com", "graph.microsoft.com", "management.azure.com"],
        }
        self.assertEqual([], validate_manifest(manifest))

    def test_network_approval_rejects_wildcard_destinations(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        manifest["approvals"]["networkProfile"]["approvedMicrosoftApiFqdns"] = [
            "*.microsoft.com"]
        self.assertTrue(
            any("exact hostnames" in error for error in validate_manifest(manifest)))

    def test_production_network_subnets_must_be_contained_and_nonoverlapping(self) -> None:
        manifest = base_manifest()
        manifest.update(environmentClass="production",
                        serviceBusSku="Premium", approvals=production_approvals())
        manifest["networkPlan"]["apiAca"] = "10.20.1.0/24"
        errors = validate_manifest(manifest)
        self.assertTrue(any(
            "networkPlan.webAca overlaps networkPlan.apiAca" in error for error in errors))
        manifest["networkPlan"]["apiAca"] = "10.30.0.0/23"
        errors = validate_manifest(manifest)
        self.assertTrue(
            any("apiAca must be contained" in error for error in errors))

    def test_standalone_mode_requires_one_valid_bound_tenant(self) -> None:
        manifest = base_manifest()
        manifest["deploymentMode"] = "standalone"
        self.assertTrue(
            any("standaloneTenantId" in error for error in validate_manifest(manifest)))
        manifest["standaloneTenantId"] = "10000000-0000-0000-0000-000000000001"
        self.assertEqual([], validate_manifest(manifest))

    def test_promotion_requires_full_immutable_image_digests(self) -> None:
        manifest = {
            "schemaVersion": "1.0",
            "sourceCommit": "b" * 40,
            "testsPassed": True,
            "provenanceArtifact": "build-metadata.json",
            "promotionStatus": "eligible",
            "promotionReferences": {
                name: f"ghcr.io/example/{name}@{VALID_DIGEST}"
                for name in ("web", "api", "assessment", "remediation")
            },
        }
        self.assertEqual([], validate_release_manifest(manifest))
        manifest["promotionReferences"]["api"] = "ghcr.io/example/api:main"
        self.assertTrue(any(
            "immutable api image digest" in error for error in validate_release_manifest(manifest)))

    def test_blocked_candidate_cannot_be_promoted_by_a_mutable_tag(self) -> None:
        manifest = {
            "schemaVersion": "1.0",
            "sourceCommit": "c" * 40,
            "testsPassed": True,
            "provenanceArtifact": "build-metadata.json",
            "promotionStatus": "blocked-no-registry-auth",
            "blockedReason": "No registry authentication was configured.",
            "promotionReferences": {},
            "localImageConfigDigests": {"web": VALID_DIGEST},
        }
        self.assertEqual([], validate_release_manifest(manifest))
        manifest["promotionReferences"] = {"web": "ghcr.io/example/web:latest"}
        self.assertTrue(any(
            "must not contain promotion references" in error for error in validate_release_manifest(manifest)))

    def test_release_artifacts_keep_local_config_digests_out_of_promotion_references(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output_directory = Path(temporary_directory)
            errors = create_artifacts(
                "d" * 40,
                "example/platform",
                "run-123",
                True,
                {"web": VALID_DIGEST, "api": VALID_DIGEST},
                {},
                output_directory,
            )
            self.assertEqual([], errors)
            release = json.loads(
                (output_directory / "release-manifest.json").read_text(encoding="utf-8"))
            build_metadata = json.loads(
                (output_directory / "build-metadata.json").read_text(encoding="utf-8"))
            self.assertEqual("blocked-no-registry-auth",
                             release["promotionStatus"])
            self.assertEqual({}, release["promotionReferences"])
            self.assertEqual("d" * 40, build_metadata["sourceCommit"])
            self.assertEqual("unsigned-build-metadata", build_metadata["kind"])

    def test_promotion_writer_records_only_actual_registry_digest_references(self) -> None:
        references = {
            name: f"ghcr.io/example/{name}@{VALID_DIGEST}"
            for name in ("web", "api", "assessment", "remediation")
        }
        with tempfile.TemporaryDirectory() as temporary_directory:
            output_directory = Path(temporary_directory)
            errors = create_artifacts(
                "e" * 40, "example/platform", "run-456", True, {}, references, output_directory)
            self.assertEqual([], errors)
            release = json.loads(
                (output_directory / "release-manifest.json").read_text(encoding="utf-8"))
            build_metadata = json.loads(
                (output_directory / "build-metadata.json").read_text(encoding="utf-8"))
            self.assertEqual("eligible", release["promotionStatus"])
            self.assertEqual(references, release["promotionReferences"])
            self.assertTrue(all(entry["registryImageReference"]
                            for entry in build_metadata["images"].values()))

    def test_cost_template_is_unpriced_and_cannot_be_mistaken_for_a_quote(self) -> None:
        estimate = json.loads(
            (ROOT / "infra/azure/cost-estimate.template.json").read_text(encoding="utf-8"))
        self.assertEqual("unpriced-template", estimate["status"])
        self.assertIsNone(estimate["monthlyEstimate"]["amount"])
        self.assertIn("not a quote", estimate["notice"].lower())

    def test_nuget_scan_finds_transitive_advisories_and_accepts_empty_reports(self) -> None:
        report = {
            "projects": [{
                "path": "api/Platform.csproj",
                "frameworks": [{
                    "framework": "net10.0",
                    "topLevelPackages": [],
                    "transitivePackages": [{
                        "id": "Example.Dependency",
                        "resolvedVersion": "1.2.3",
                        "vulnerabilities": [{"severity": "High", "url": "https://example.invalid/advisory"}],
                    }],
                }],
            }],
        }
        self.assertIn("Example.Dependency 1.2.3: High",
                      find_vulnerabilities(report)[0])
        self.assertEqual([], find_vulnerabilities(
            {"projects": [{"path": "empty.csproj", "frameworks": []}]}))

    def test_compatibility_fixture_does_not_claim_a_prior_release(self) -> None:
        baseline = json.loads(
            (ROOT / "infra/validation/compatibility-baseline.json").read_text(encoding="utf-8"))
        contracts = (
            ROOT / "api/M365.SecurityAssessment.Domain/Contracts.cs").read_text(encoding="utf-8")
        migration = (
            ROOT / "api/M365.SecurityAssessment.Persistence/Migrations/001_storage_audit.sql").read_text(encoding="utf-8")
        role_bootstrap = (
            ROOT / "infra/azure/postgres/bootstrap-application-role.sql").read_text(encoding="utf-8").lower()
        self.assertEqual("current-source-only", baseline["baselineKind"])
        self.assertIsNone(baseline["priorSupportedRelease"])
        self.assertEqual("not-testable-no-prior-supported-release",
                         baseline["priorReleaseCompatibility"])
        self.assertIn('public const string JobEnvelope = "1.0";', contracts)
        self.assertEqual("1.0", baseline["jobEnvelopeContractVersion"])
        self.assertIn("BEGIN;", migration)
        self.assertIn("COMMIT;", migration)
        self.assertIn("FORCE ROW LEVEL SECURITY", migration)
        self.assertNotRegex(
            migration, r"(?im)^\s*DROP\s+(TABLE|COLUMN|SCHEMA|TYPE)\b")
        for required in ("nologin", "nosuperuser", "nocreatedb", "nocreaterole", "nobypassrls"):
            self.assertIn(required, role_bootstrap)

    def test_bicep_has_private_data_and_separated_web_api_ingress(self) -> None:
        template = (ROOT / "infra/azure/main.bicep").read_text(encoding="utf-8")
        worker_module = (
            ROOT / "infra/azure/modules/customer-worker.bicep").read_text(encoding="utf-8")
        self.assertIn("publicNetworkAccess: 'Disabled'", template)
        self.assertIn("keySource: 'Microsoft.Storage'", template)
        self.assertIn("name: 'require_secure_transport'", template)
        self.assertIn("tenantEvidenceLifecycleRules", template)
        self.assertIn(
            "daysAfterCreationGreaterThan: evidenceRetentionDays", template)
        self.assertIn("delegatedSubnetResourceId: postgresSubnet.id", template)
        self.assertIn("internal: true", template)
        self.assertIn("external: false", template)
        self.assertIn("external: true", template)
        self.assertIn("module assessmentWorkerStacks", template)
        self.assertIn("module remediationWorkerStacks", template)
        self.assertIn(
            "binding.assessmentManifestApproval.approved == true", template)
        self.assertIn(
            "binding.remediationManifestApproval.approved == true", template)
        self.assertIn("scope: queue", worker_module)
        self.assertIn("scope: evidenceContainer", worker_module)
        self.assertIn("scope: credentialSecret", worker_module)
        self.assertNotIn("Microsoft.Compute/virtualMachines",
                         template + worker_module)


if __name__ == "__main__":
    unittest.main()
