"""Validate deployment inputs without contacting Azure or creating resources."""

from __future__ import annotations

import argparse
import ipaddress
import json
import re
import sys
from datetime import datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any


IMAGE_DIGEST = re.compile(r"^.+@sha256:[0-9a-f]{64}$")
SHA256 = re.compile(r"^sha256:[0-9a-f]{64}$")
MANIFEST_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
TENANT_ID = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")
FQDN = re.compile(
    r"^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))+$")
SENSITIVE_KEYS = {
    "password",
    "token",
    "privatekey",
    "connectionstring",
    "secretvalue",
    "credentialvalue",
    "clientsecret",
}
IDENTITY_ROLES = {
    "web": set(),
    "api": {"servicebus-send-assessment", "servicebus-send-remediation", "keyvault-crypto"},
    "assessment": {"servicebus-receive-assessment", "blob-write-evidence"},
    "remediation": {"servicebus-receive-remediation", "blob-write-evidence"},
}
PRODUCTION_APPROVALS = (
    "region",
    "networkProfile",
    "premiumCapacityCost",
    "budget",
    "retention",
    "productionSlo",
)
PRODUCTION_SLO_FIELDS = (
    "availabilityPercent",
    "queueAgeAlertSeconds",
    "jobTimeoutSeconds",
    "perCustomerConcurrency",
    "maxTransientRetries",
    "recoveryRtoHours",
    "recoveryRpoMinutes",
)
NETWORK_SUBNETS = ("webAca", "apiAca", "postgres", "privateEndpoints")


def _is_nonempty_string(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def _contains_sensitive_value(value: Any, path: str = "manifest") -> list[str]:
    errors: list[str] = []
    if isinstance(value, dict):
        for key, child in value.items():
            normalized_key = key.lower().replace("_", "")
            if normalized_key in SENSITIVE_KEYS or normalized_key.endswith(("password", "clientsecret", "secretvalue", "credentialvalue")):
                errors.append(
                    f"{path}.{key} must not contain credential material")
            errors.extend(_contains_sensitive_value(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            errors.extend(_contains_sensitive_value(child, f"{path}[{index}]"))
    return errors


def _validate_approval(name: str, approval: Any, errors: list[str]) -> None:
    if not isinstance(approval, dict) or approval.get("approved") is not True:
        errors.append(
            f"production approval '{name}' must be an explicit approved record")
        return
    for field in ("reference", "approvedBy"):
        if not _is_nonempty_string(approval.get(field)):
            errors.append(f"production approval '{name}' requires {field}")
    timestamp = approval.get("approvedAtUtc")
    if not isinstance(timestamp, str) or not timestamp.strip():
        errors.append(f"production approval '{name}' requires approvedAtUtc")
    else:
        try:
            parsed = datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
            offset = parsed.utcoffset()
            if offset is None or offset.total_seconds() != 0:
                errors.append(
                    f"production approval '{name}' approvedAtUtc must be UTC")
        except ValueError:
            errors.append(
                f"production approval '{name}' approvedAtUtc must be an ISO-8601 timestamp")


def _validate_network_approval(approval: Any, errors: list[str], stage: str) -> None:
    if not isinstance(approval, dict):
        return
    for field in ("profileId", "egressPolicyReference", "firewallPrivateIp"):
        if not _is_nonempty_string(approval.get(field)):
            errors.append(f"{stage} network approval requires {field}")
    firewall_ip = approval.get("firewallPrivateIp")
    if isinstance(firewall_ip, str) and firewall_ip.strip():
        try:
            if not ipaddress.ip_address(firewall_ip).is_private:
                errors.append(
                    f"{stage} firewallPrivateIp must be a private address")
        except ValueError:
            errors.append(
                f"{stage} firewallPrivateIp must be a valid IP address")
    destinations = approval.get("approvedMicrosoftApiFqdns")
    if not isinstance(destinations, list) or not destinations:
        errors.append(
            f"{stage} network approval requires an explicit approvedMicrosoftApiFqdns list")
    else:
        for destination in destinations:
            if not isinstance(destination, str) or not FQDN.fullmatch(destination) or "*" in destination:
                errors.append(
                    f"{stage} egress destinations must be exact hostnames without wildcards or URL schemes")


def _validate_network_plan(plan: Any, errors: list[str], required: bool) -> None:
    if not isinstance(plan, dict):
        if required:
            errors.append(
                "networkPlan with a VNet and dedicated workload/data/private-endpoint subnets is required")
        return
    try:
        vnet = ipaddress.ip_network(plan.get("vnetCidr", ""), strict=True)
    except (TypeError, ValueError):
        errors.append("networkPlan.vnetCidr must be a canonical network CIDR")
        return

    subnets: list[tuple[str, Any]] = []
    for name in NETWORK_SUBNETS:
        try:
            subnet = ipaddress.ip_network(plan.get(name, ""), strict=True)
        except (TypeError, ValueError):
            errors.append(
                f"networkPlan.{name} must be a canonical subnet CIDR")
            continue
        if isinstance(subnet, ipaddress.IPv4Network) and isinstance(vnet, ipaddress.IPv4Network):
            contained = subnet.subnet_of(vnet)
        elif isinstance(subnet, ipaddress.IPv6Network) and isinstance(vnet, ipaddress.IPv6Network):
            contained = subnet.subnet_of(vnet)
        else:
            contained = False
        if not contained:
            errors.append(
                f"networkPlan.{name} must be contained in networkPlan.vnetCidr")
        subnets.append((name, subnet))

    for index, (first_name, first_subnet) in enumerate(subnets):
        for second_name, second_subnet in subnets[index + 1:]:
            if first_subnet.version == second_subnet.version and first_subnet.overlaps(second_subnet):
                errors.append(
                    f"networkPlan.{first_name} overlaps networkPlan.{second_name}")


def validate_manifest(manifest: Any) -> list[str]:
    errors: list[str] = []
    if not isinstance(manifest, dict):
        return ["manifest must be a JSON object"]

    errors.extend(_contains_sensitive_value(manifest))
    if manifest.get("environmentClass") not in {"development-test", "pre-production", "production"}:
        errors.append(
            "environmentClass must be development-test, pre-production, or production")
    if manifest.get("deploymentMode") not in {"central", "standalone"}:
        errors.append("deploymentMode must be central or standalone")
    elif manifest.get("deploymentMode") == "standalone" and not TENANT_ID.fullmatch(str(manifest.get("standaloneTenantId", ""))):
        errors.append(
            "standalone deployment requires exactly one valid standaloneTenantId")

    location = manifest.get("location")
    if not _is_nonempty_string(location):
        errors.append(
            "location is required and must be an explicitly selected Azure region")
    _validate_network_plan(
        manifest.get("networkPlan"),
        errors,
        manifest.get("environmentClass") in {"pre-production", "production"},
    )

    worker_bindings = manifest.get("customerWorkerBindings")
    if not isinstance(worker_bindings, list):
        errors.append(
            "customerWorkerBindings must be an explicit per-customer list; use an empty list to provision no workers")
        worker_bindings = []
    purpose_tenants = {"assessment": set(), "remediation": set()}
    purpose_digests = {"assessment": set(), "remediation": set()}
    bound_tenants: set[str] = set()
    for index, binding in enumerate(worker_bindings):
        if not isinstance(binding, dict):
            errors.append(f"customerWorkerBindings[{index}] must be an object")
            continue
        tenant_id = binding.get("tenantId")
        if not isinstance(tenant_id, str) or not TENANT_ID.fullmatch(tenant_id):
            errors.append(
                f"customerWorkerBindings[{index}].tenantId must be a valid tenant UUID")
            continue
        tenant_id = tenant_id.lower()
        if tenant_id in bound_tenants:
            errors.append(
                "customerWorkerBindings must contain exactly one entry per tenant")
        bound_tenants.add(tenant_id)
        if manifest.get("deploymentMode") == "standalone" and tenant_id != str(manifest.get("standaloneTenantId", "")).lower():
            errors.append(
                "standalone worker bindings must match the configured standaloneTenantId")
        for purpose in ("assessment", "remediation"):
            digest = binding.get(f"{purpose}ManifestDigest", "")
            if not isinstance(digest, str):
                errors.append(
                    f"customerWorkerBindings[{index}].{purpose}ManifestDigest must be a string")
                continue
            if digest:
                if not MANIFEST_DIGEST.fullmatch(digest):
                    errors.append(
                        f"customerWorkerBindings[{index}].{purpose}ManifestDigest must be an immutable SHA-256 digest")
                    continue
                _validate_approval(
                    f"{purpose} adapter manifest for tenant {tenant_id}",
                    binding.get(f"{purpose}ManifestApproval"),
                    errors,
                )
                purpose_tenants[purpose].add(tenant_id)
                purpose_digests[purpose].add(digest)
            elif binding.get(f"{purpose}ManifestApproval"):
                errors.append(
                    f"customerWorkerBindings[{index}] cannot include a {purpose} manifest approval without a digest")

    windows_digests = manifest.get("windowsWorkerManifestDigests", [])
    if not isinstance(windows_digests, list):
        errors.append("windowsWorkerManifestDigests must be a list")
    elif windows_digests:
        errors.append(
            "Windows worker manifests are blocked: this candidate has no separately approved isolated Windows VM host")

    images = manifest.get("images")
    if not isinstance(images, dict):
        errors.append(
            "images must contain immutable references for web, api, assessment, and remediation")
    else:
        for name in ("web", "api", "assessment", "remediation"):
            if not isinstance(images.get(name), str) or not IMAGE_DIGEST.fullmatch(images[name]):
                errors.append(
                    f"images.{name} must use an immutable @sha256 image digest")

    service_bus_sku = manifest.get("serviceBusSku")
    if service_bus_sku not in {"Standard", "Premium"}:
        errors.append("serviceBusSku must be Standard or Premium")

    private_access = manifest.get("privateAccess")
    if not isinstance(private_access, dict):
        errors.append(
            "privateAccess must describe private service access and ingress boundaries")
    else:
        for service in ("postgres", "blob", "keyVault", "serviceBus"):
            if private_access.get(service) is not True:
                errors.append(f"privateAccess.{service} must be true")
        if private_access.get("apiIngressPublic") is not False:
            errors.append("privateAccess.apiIngressPublic must be false")
        if private_access.get("separateWebPerimeter") is not True:
            errors.append("privateAccess.separateWebPerimeter must be true")

    postgres = manifest.get("postgresAppRole")
    if not isinstance(postgres, dict) or postgres.get("name") != "platform_app":
        errors.append(
            "postgresAppRole must identify the scoped platform_app role")
    else:
        for flag in ("superuser", "createDatabase", "createRole", "bypassRls"):
            if postgres.get(flag) is not False:
                errors.append(f"postgresAppRole.{flag} must be false")
        if postgres.get("rowLevelSecurity") is not True or postgres.get("forceRowLevelSecurity") is not True:
            errors.append(
                "postgresAppRole must require enabled and forced row-level security")

    identities = manifest.get("identities")
    if not isinstance(identities, dict):
        errors.append(
            "identities must define scoped web, api, assessment, and remediation identities")
        identities = {}
    for identity, allowed_roles in IDENTITY_ROLES.items():
        entry = identities.get(identity)
        if not isinstance(entry, dict):
            errors.append(f"identities.{identity} is required")
            continue
        roles = entry.get("roles")
        if not isinstance(roles, list) or any(not isinstance(role, str) for role in roles):
            errors.append(f"identities.{identity}.roles must be a list")
        else:
            role_set = set(roles)
            if len(role_set) != len(roles):
                errors.append(
                    f"identities.{identity}.roles must not contain duplicates")
            if role_set - allowed_roles:
                errors.append(
                    f"identities.{identity}.roles contains an unapproved or overbroad grant")
        tenant_ids = entry.get("credentialTenantIds", [])
        if not isinstance(tenant_ids, list) or any(not isinstance(tenant_id, str) for tenant_id in tenant_ids):
            errors.append(
                f"identities.{identity}.credentialTenantIds must be a list of tenant IDs")
            continue
        if len(set(tenant_ids)) != len(tenant_ids):
            errors.append(
                f"identities.{identity}.credentialTenantIds must not contain duplicates")
        for tenant_id in tenant_ids:
            if not TENANT_ID.fullmatch(tenant_id):
                errors.append(
                    f"identities.{identity} contains an invalid credential tenant scope")
        expected_purpose = identity if identity in {
            "assessment", "remediation"} else None
        if entry.get("credentialPurpose") != expected_purpose:
            errors.append(
                f"identities.{identity}.credentialPurpose must be {expected_purpose or 'unset'}")
        manifest_digests = entry.get("verifiedManifestDigests", [])
        if not isinstance(manifest_digests, list) or any(not isinstance(digest, str) or not MANIFEST_DIGEST.fullmatch(digest) for digest in manifest_digests):
            errors.append(
                f"identities.{identity}.verifiedManifestDigests must contain only immutable SHA-256 digests")
        else:
            if len(set(manifest_digests)) != len(manifest_digests):
                errors.append(
                    f"identities.{identity}.verifiedManifestDigests must not contain duplicates")
            if identity in {"assessment", "remediation"} and set(manifest_digests) != purpose_digests[identity]:
                errors.append(
                    f"identities.{identity}.verifiedManifestDigests must exactly match approved customer bindings")
            if identity in {"web", "api"} and manifest_digests:
                errors.append(
                    f"identities.{identity} must not receive worker manifest bindings")
        if identity in {"assessment", "remediation"}:
            expected_tenants = purpose_tenants[identity]
            if {tenant_id.lower() for tenant_id in tenant_ids if isinstance(tenant_id, str)} != expected_tenants:
                errors.append(
                    f"identities.{identity}.credentialTenantIds must exactly match that purpose's approved customer bindings")
            expected_roles = allowed_roles if expected_tenants else set()
            if isinstance(roles, list) and set(roles) != expected_roles:
                errors.append(
                    f"identities.{identity}.roles must be absent until a matching verified manifest exists")
        if identity in {"web", "api"} and tenant_ids:
            errors.append(
                f"identities.{identity} must not receive customer connector credentials")

    approvals = manifest.get("approvals", {})
    if not isinstance(approvals, dict):
        errors.append("approvals must be an object")
        approvals = {}

    if manifest.get("environmentClass") == "pre-production":
        _validate_approval("networkProfile", approvals.get(
            "networkProfile"), errors)
        _validate_network_approval(approvals.get(
            "networkProfile"), errors, "pre-production")

    if manifest.get("environmentClass") == "production":
        if service_bus_sku != "Premium":
            errors.append("production requires Service Bus Premium")
        for name in PRODUCTION_APPROVALS:
            _validate_approval(name, approvals.get(name), errors)
        region_approval = approvals.get("region", {})
        if isinstance(region_approval, dict) and region_approval.get("approvedRegion") != location:
            errors.append("approved region must exactly match location")
        network = approvals.get("networkProfile", {})
        if isinstance(network, dict):
            _validate_network_approval(network, errors, "production")
        capacity = approvals.get("premiumCapacityCost", {})
        if isinstance(capacity, dict):
            if capacity.get("sku") != "Premium" or not isinstance(capacity.get("messagingUnits"), int) or capacity.get("messagingUnits", 0) < 1:
                errors.append(
                    "Premium capacity approval must specify Premium and positive messagingUnits")
            if not _is_nonempty_string(capacity.get("workloadProfileId")) or not _is_nonempty_string(capacity.get("estimateSource")):
                errors.append(
                    "Premium capacity approval must identify its workload profile and estimate source")
            try:
                monthly_estimate = Decimal(
                    str(capacity.get("estimatedMonthlyCost", "")))
                if not monthly_estimate.is_finite() or monthly_estimate <= 0:
                    raise InvalidOperation
            except (InvalidOperation, ValueError):
                errors.append(
                    "Premium capacity approval requires a positive, reviewed monthly estimate")
            if not _is_nonempty_string(capacity.get("currency")):
                errors.append(
                    "Premium capacity approval requires estimate currency")
        budget = approvals.get("budget", {})
        if isinstance(budget, dict):
            try:
                monthly_limit = Decimal(str(budget.get("monthlyLimit", "")))
                if not monthly_limit.is_finite() or monthly_limit <= 0:
                    raise InvalidOperation
            except (InvalidOperation, ValueError):
                errors.append(
                    "budget approval requires a positive monthlyLimit")
            if not _is_nonempty_string(budget.get("currency")):
                errors.append("budget approval requires currency")
        retention = approvals.get("retention", {})
        if isinstance(retention, dict):
            for field in ("evidenceDays", "auditDays", "backupDays"):
                if not isinstance(retention.get(field), int) or retention.get(field, 0) < 1:
                    errors.append(
                        f"retention approval requires positive {field}")
            if retention.get("postgresGeoRedundantBackup") not in {"Enabled", "Disabled"}:
                errors.append(
                    "retention approval requires an explicit PostgreSQL geo-redundant backup decision")
        slo = approvals.get("productionSlo", {})
        if isinstance(slo, dict):
            for field in PRODUCTION_SLO_FIELDS:
                value = slo.get(field)
                if not isinstance(value, (int, float)) or value <= 0:
                    errors.append(
                        f"production SLO approval requires positive {field}")
            if isinstance(slo.get("availabilityPercent"), (int, float)) and slo["availabilityPercent"] > 100:
                errors.append(
                    "production SLO availabilityPercent must not exceed 100")

    return errors


def validate_release_manifest(manifest: Any) -> list[str]:
    if not isinstance(manifest, dict):
        return ["release manifest must be a JSON object"]

    errors = _contains_sensitive_value(manifest)
    if manifest.get("schemaVersion") != "1.0":
        errors.append("release manifest schemaVersion must be 1.0")
    if not isinstance(manifest.get("sourceCommit"), str) or not COMMIT.fullmatch(manifest["sourceCommit"]):
        errors.append(
            "release manifest sourceCommit must be a full 40-character commit ID")
    if manifest.get("testsPassed") is not True:
        errors.append("promotion requires passing test evidence")
    if not isinstance(manifest.get("provenanceArtifact"), str) or not manifest["provenanceArtifact"].strip():
        errors.append(
            "release manifest must identify its build provenance artifact")

    promotion_status = manifest.get("promotionStatus")
    references = manifest.get("promotionReferences")
    if not isinstance(references, dict):
        errors.append("promotionReferences must be an object")
        references = {}
    if promotion_status == "eligible":
        for name in ("web", "api", "assessment", "remediation"):
            reference = references.get(name)
            if not isinstance(reference, str) or not IMAGE_DIGEST.fullmatch(reference):
                errors.append(
                    f"eligible promotion requires an immutable {name} image digest reference")
    elif promotion_status in {"blocked-no-registry-auth", "blocked-tests"}:
        if references:
            errors.append(
                "blocked release manifests must not contain promotion references")
        if not _is_nonempty_string(manifest.get("blockedReason")):
            errors.append("blocked release manifest requires blockedReason")
    else:
        errors.append(
            "promotionStatus must be eligible or an explicit blocked status")

    local_digests = manifest.get("localImageConfigDigests", {})
    if not isinstance(local_digests, dict):
        errors.append("localImageConfigDigests must be an object")
    else:
        for name, digest in local_digests.items():
            if name not in {"web", "api", "assessment", "remediation"} or not isinstance(digest, str) or not SHA256.fullmatch(digest):
                errors.append(
                    "localImageConfigDigests may contain only recognized image config SHA-256 values")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--release-manifest", action="store_true")
    arguments = parser.parse_args()
    try:
        manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"Invalid deployment manifest: {error}", file=sys.stderr)
        return 2

    validator = validate_release_manifest if arguments.release_manifest else validate_manifest
    errors = validator(manifest)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        print(
            f"Rejected {len(errors)} deployment input issue(s); no resources were deployed.", file=sys.stderr)
        return 1

    print("Deployment inputs validated; no Azure resources were contacted or deployed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
