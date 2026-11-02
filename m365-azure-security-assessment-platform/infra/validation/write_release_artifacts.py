"""Create a CI candidate or digest-promotion manifest and its provenance record."""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

try:
    from .validate_deployment import validate_release_manifest
except ImportError:
    from validate_deployment import validate_release_manifest


IMAGE_NAMES = ("web", "api", "assessment", "remediation")
BASE_IMAGES = {
    "web": "node:26.12.0-bookworm-slim",
    "api": ["mcr.microsoft.com/dotnet/sdk:10.0.403", "mcr.microsoft.com/dotnet/aspnet:10.0.14"],
    "assessment": ["mcr.microsoft.com/dotnet/sdk:10.0.403", "mcr.microsoft.com/dotnet/aspnet:10.0.14"],
    "remediation": ["mcr.microsoft.com/dotnet/sdk:10.0.403", "mcr.microsoft.com/dotnet/aspnet:10.0.14"],
}


def parse_assignments(values: list[str], option: str) -> dict[str, str]:
    parsed: dict[str, str] = {}
    for value in values:
        name, separator, content = value.partition("=")
        if not separator or name not in IMAGE_NAMES or not content or name in parsed:
            raise ValueError(f"{option} expects unique name=value pairs for {', '.join(IMAGE_NAMES)}")
        parsed[name] = content
    return parsed


def create_artifacts(
    source_commit: str,
    repository: str,
    run_id: str,
    tests_passed: bool,
    local_digests: dict[str, str],
    promotion_references: dict[str, str],
    output_directory: Path,
) -> list[str]:
    eligible = bool(promotion_references)
    manifest: dict[str, Any] = {
        "schemaVersion": "1.0",
        "sourceCommit": source_commit,
        "testsPassed": tests_passed,
        "provenanceArtifact": "build-metadata.json",
        "promotionStatus": "eligible" if eligible else "blocked-no-registry-auth",
        "promotionReferences": promotion_references,
    }
    if local_digests:
        manifest["localImageConfigDigests"] = local_digests
    if not eligible:
        manifest["blockedReason"] = "No authenticated registry image manifest digests were produced; local image config digests are not promotion references."

    errors = validate_release_manifest(manifest)
    if errors:
        return errors

    now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    build_metadata = {
        "schemaVersion": "1.0",
        "kind": "unsigned-build-metadata",
        "generatedAtUtc": now,
        "repository": repository,
        "sourceCommit": source_commit,
        "runId": run_id,
        "images": {
            name: {
                "baseImages": BASE_IMAGES[name],
                "localImageConfigDigest": local_digests.get(name),
                "registryImageReference": promotion_references.get(name),
            }
            for name in IMAGE_NAMES
        },
    }

    output_directory.mkdir(parents=True, exist_ok=True)
    (output_directory / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (output_directory / "build-metadata.json").write_text(json.dumps(build_metadata, indent=2) + "\n", encoding="utf-8")
    return []


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--tests-passed", action="store_true")
    parser.add_argument("--local-digest", action="append", default=[])
    parser.add_argument("--promotion-reference", action="append", default=[])
    parser.add_argument("--output-directory", required=True, type=Path)
    arguments = parser.parse_args()

    try:
        local_digests = parse_assignments(arguments.local_digest, "--local-digest")
        promotion_references = parse_assignments(arguments.promotion_reference, "--promotion-reference")
        if bool(local_digests) and bool(promotion_references):
            raise ValueError("a manifest cannot mix local image config digests and registry promotion references")
    except ValueError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2

    errors = create_artifacts(
        arguments.source_commit,
        arguments.repository,
        arguments.run_id,
        arguments.tests_passed,
        local_digests,
        promotion_references,
        arguments.output_directory,
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("Release manifest and provenance artifact written; no deployment was attempted.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())