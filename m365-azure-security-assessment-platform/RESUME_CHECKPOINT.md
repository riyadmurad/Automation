# Resume Checkpoint

Updated: 2026-11-02. Read this file first; do not reconstruct this session from the full development log.

## Stop Boundary

- Latest user instruction: finish ONLY Application Foundation and pause for later work.
- Change: `m365-azure-security-baseline-platform`; application root: `m365-azure-security-assessment-platform/`.
- Tasks 1.1-1.5 are accepted. Recorded progress: **5/107**, with **102 tasks still open**.
- Do not start tasks 2-18 until the user explicitly resumes them. Draft files for later groups already exist; do not delete, overwrite, or mark them complete merely because they compile.
- Authoritative checklist: `../openspec/changes/m365-azure-security-baseline-platform/tasks.md` relative to this application directory.
- No Azure resources were deployed, no live tenant tests were authorized, and no customer configuration was changed.

## Accepted Foundation

| Task | Acceptance Evidence                                                                                                                 |
| ---- | ----------------------------------------------------------------------------------------------------------------------------------- |
| 1.1  | Next.js/.NET projects, workers, tests and runtime pins exist; current solution builds                                               |
| 1.2  | Versioned contracts, two synthetic customers, tenant/purpose/organization binding and negative contract tests                       |
| 1.3  | Frozen 13-control candidate 0.1.1 with retrieval/source pins, normalized rules, prerequisites and scenario/task/fixture mapping     |
| 1.4  | Root/platform setup guides and safe synthetic build/test commands                                                                   |
| 1.5  | All 13 controls across eight required outcomes, with exact status/reason checks plus evidence/schema/provenance/inventory negatives |

Current candidate: `baselines/qualified/pack-0.1.1.json`.
Freeze pin: `baselines/qualified/freeze-manifest-0.1.1.json`.
Pack-file SHA-256: `E9113FA7E9FF4199A420E05E346FDDFE820F68FB05EBEEA0A82FB89BC7EE3F42` (current 0.1.1 file bytes; parsed source content matches the committed pre-format candidate).

Version 0.1.0 and its manifest remain unchanged as history. Version 0.1.1 corrects PP-001 to allow extra covered environments while requiring all independently approved expected environments to be covered. Do not edit frozen versions in place; create a new pack version and pin for future changes.

All checked-in controls remain pending review and manual/unverified. Source hashes are retrieval pins, not publisher-authenticated versions, approved policy or qualified API operations. ENT points at a retrieved CISA landing page, not an exact release/control file; MDCA's source mapping remains unresolved. Test-only qualified copies exercise pass/fail without activating the checked-in pack.

## Verified At This Checkpoint

- Assessment foundation suite: **335 passed**, none failed/skipped. Includes 104 qualified outcome cases (13 x 8), pending-pack cases, provenance/identity/evidence validation and PP-001 subset regressions.
- Foundation API/domain suite: **58 passed**, none failed/skipped.
- Solution build: passed, including existing worker and draft module/test projects.
- Frontend: lint and typecheck passed; **69 tests passed** across eight files.
- Prior browser milestone: six desktop/mobile journeys passed. They were not rerun for this test/catalog-only closure.
- Prior production build/HTTP gate passed; no new production build was run while the development server was active.
- Preview observed listening on loopback port 3000. Recheck availability rather than relying on an old process or terminal ID.
- .NET executable on this machine: `C:\Users\DEV\AppData\Local\Microsoft\dotnet\dotnet.exe`, SDK 10.0.403. Node 26.12.0. Docker Linux engine available when last checked.

## Minimal Resume Reads

1. This file, root `AGENTS.md`, and current Git status.
2. `openspec instructions apply --change m365-azure-security-baseline-platform --json` for actual task state and file locations.
3. Only the next authorized task's spec/design section and owning module README/source. Read the full log only if a concrete discrepancy needs history.

Foundation files: `api/M365.SecurityAssessment.Domain/`, `api/M365.SecurityAssessment.Assessment/`, `baselines/`, `tests/backend/M365.SecurityAssessment.Tests/`, and `tests/backend/M365.SecurityAssessment.Assessment.Tests/`.
Principal fixture suite: `tests/backend/M365.SecurityAssessment.Assessment.Tests/AssessmentAcceptanceTests.cs`.

From the repository root, quick verification is:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet test ./m365-azure-security-assessment-platform/tests/backend/M365.SecurityAssessment.Assessment.Tests/M365.SecurityAssessment.Assessment.Tests.csproj --no-restore
& $dotnet test ./m365-azure-security-assessment-platform/tests/backend/M365.SecurityAssessment.Tests/M365.SecurityAssessment.Tests.csproj --no-restore
& $dotnet build ./m365-azure-security-assessment-platform/M365.SecurityAssessment.slnx --no-restore
npm run lint --prefix ./m365-azure-security-assessment-platform/web
npm run typecheck --prefix ./m365-azure-security-assessment-platform/web
npm test --prefix ./m365-azure-security-assessment-platform/web
```

Restore dependencies with the committed lockfiles on a fresh machine; use the SDK on PATH if not installed at the local path above. Stop the owning dev server before a production build because both share `.next`. New installs must use latest stable compatible Next.js/required packages, pin exact installed versions and commit lockfiles; do not silently downgrade or suppress validation.

## Existing Later-Group Drafts

Preserve `api/M365.SecurityAssessment.Authentication/`, `Persistence/`, `Runtime/` (full project names have the `M365.SecurityAssessment.` prefix), their test projects, web auth/BFF routes, API composition, Azure/Docker validation files and `.github/workflows/platform-ci.yml`. They were created before the scope narrowed and remain unaccepted. Some read-only API routes are wired with fail-closed configuration; the local fixture UI is not proof of production authorization or data integration.

Earlier delegate-reported module results: PostgreSQL persistence 8, authentication 14, runtime 92 passing tests; infrastructure checks 27; source/assessment work was subsequently expanded into the foundation suite above. These counts do not constitute task-group acceptance and are not substitutes for rerunning relevant checks when that group resumes.

## Known Follow-Up Risks

- API integration, real customer onboarding/permission-health/credential rotation and multi-instance Node/PostgreSQL auth-session verification need completion and independent review.
- A privileged database owner can recompute the current unkeyed audit chain; independently protected checkpoints/archives are absent.
- Connection permission checks need an empty-required-scope denial case before privileged use.
- Bicep production approval/Premium/network conditions need enforcement bound to the exact deployed parameters; controlled egress is not proved end-to-end.
- CI currently attests manifests rather than each OCI image; action/base-image inputs require reviewed immutable pins. Application Insights IP masking needs review (`DisableIpMasking` finding).
- Source approval, service-operation qualification, full-text update review, Windows hosts, Service Bus/persistent runtime adapters, live conditional writes and recovery proofs remain unfinished. The mutation allowlist stays empty.
- Deployment region/network/cost/SLO/retention approvals and named disposable live-test authorization are still required. Never infer them from a request to resume coding.

Before publishing or staging everything, inspect the current worktree. Prior interrupted work included untracked repository-root `package.json`/lockfile and other later-group drafts; preserve them and determine ownership rather than deleting or committing blindly. No secrets belong in this checkpoint or source. Consult `git log -1 --oneline` and current status for the actual saved commit instead of assuming historical commit IDs remain current.