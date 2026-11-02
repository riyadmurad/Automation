"""Run the .NET package vulnerability report and fail on any reported advisory."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any


def find_vulnerabilities(report: Any) -> list[str]:
    if not isinstance(report, dict) or not isinstance(report.get("projects"), list):
        raise ValueError("NuGet vulnerability report has an unsupported JSON shape")

    findings: list[str] = []
    for project in report["projects"]:
        if not isinstance(project, dict):
            raise ValueError("NuGet vulnerability report contains an invalid project entry")
        project_path = project.get("path", "unknown project")
        frameworks = project.get("frameworks", [])
        if not isinstance(frameworks, list):
            raise ValueError(f"NuGet vulnerability report has an invalid framework list for {project_path}")
        for framework in frameworks:
            if not isinstance(framework, dict):
                raise ValueError(f"NuGet vulnerability report has an invalid framework entry for {project_path}")
            target_framework = framework.get("framework", "unknown framework")
            for package_group in ("topLevelPackages", "transitivePackages"):
                packages = framework.get(package_group, [])
                if not isinstance(packages, list):
                    raise ValueError(f"NuGet vulnerability report has an invalid {package_group} list")
                for package in packages:
                    if not isinstance(package, dict):
                        raise ValueError(f"NuGet vulnerability report has an invalid package in {project_path}")
                    advisories = package.get("vulnerabilities", [])
                    if advisories is None:
                        advisories = []
                    if not isinstance(advisories, list):
                        raise ValueError(f"NuGet vulnerability list is invalid for package {package.get('id', 'unknown')}")
                    for advisory in advisories:
                        if isinstance(advisory, dict):
                            severity = advisory.get("severity", "unknown severity")
                            url = advisory.get("url", advisory.get("advisoryUrl", "no advisory URL"))
                        else:
                            severity = str(advisory)
                            url = "no advisory URL"
                        findings.append(
                            f"{project_path} [{target_framework}] {package.get('id', 'unknown package')} "
                            f"{package.get('resolvedVersion', 'unknown version')}: {severity} ({url})"
                        )
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    arguments = parser.parse_args()
    command = [
        arguments.dotnet,
        "list",
        str(arguments.project),
        "package",
        "--vulnerable",
        "--include-transitive",
        "--format",
        "json",
    ]
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode != 0:
        print(f"NuGet scan command failed with exit code {result.returncode}.", file=sys.stderr)
        return result.returncode
    try:
        findings = find_vulnerabilities(json.loads(result.stdout))
    except (json.JSONDecodeError, ValueError) as error:
        print(f"NuGet scan could not validate the JSON report: {error}", file=sys.stderr)
        return 2
    if findings:
        for finding in findings:
            print(f"VULNERABLE: {finding}", file=sys.stderr)
        return 1
    print("No NuGet vulnerabilities reported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())