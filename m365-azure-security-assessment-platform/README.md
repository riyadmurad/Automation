# M365 and Azure Security Assessment Platform

## Status

This repository currently contains a local, synthetic foundation, not a production security service. The UI is Next.js/React/TypeScript; the API, versioned domain contracts and worker entry points use .NET 10. PostgreSQL, Entra sign-in, customer consent/connections, durable audit storage, live assessments and remediation remain implementation tasks.

The 13 initial service/control definitions are manual and unverified. Source links are not proof that an endpoint, permission, license, baseline revision or expected value has been qualified. Matching synthetic values do not establish customer compliance. Any comparison result is separate simulation metadata; unverified controls remain manual review.

No Azure resources are deployed by local setup. No Microsoft customer credentials are required. The workers are inactive; an explicit job request exits with a blocked status.

## Prerequisites

- Node.js 26.12.0 is the validated local runtime; `.node-version` records it.
- npm with the committed lockfile.
- .NET SDK 10.0.403; `global.json` selects the supported SDK family.
- Docker Desktop with the Linux engine running for future real PostgreSQL/container tests. The synthetic and in-memory API tests do not establish PostgreSQL or RLS behavior.

If the SDK was installed with Microsoft's user-local installer, add it to the current shell only:

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet --version
```

## Local UI Preview

From the repository root:

```powershell
npm ci --prefix ./m365-azure-security-assessment-platform/web
$env:ENABLE_SYNTHETIC_PREVIEW = 'true'
npm run dev --prefix ./m365-azure-security-assessment-platform/web
```

Open http://127.0.0.1:3000. The dev command binds to loopback. Without the explicit flag the application displays its unconfigured, blocked state. Production never enables the synthetic console, even when the flag is set.

The two fixture tenants are Synthetic Customer A (`10000000-0000-0000-0000-000000000001`) and Synthetic Customer B (`20000000-0000-0000-0000-000000000002`). Local selection is a preview convenience, not production authentication or authorization.

The console provides Overview, Assessments, Findings, Coverage and Audit views with local filtering, evidence dialogs and synthetic CSV/JSON exports. Every export carries synthetic provenance. Audit examples show timestamp, authorized user, service/setting/resource, previous/requested/verified values and outcome, with secret redaction. These are illustrative events, not records of customer writes.

## Local API

From the repository root, in a separate shell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Platform__EnableSyntheticPreview = 'true'
dotnet run --no-launch-profile --project ./m365-azure-security-assessment-platform/api/M365.SecurityAssessment.Api --urls http://127.0.0.1:5000
```

- `/health` is public and contains no customer configuration.
- `/api/v1/synthetic/fixtures` lists the synthetic tenants only when explicitly enabled in Development.
- `/api/v1/synthetic/assessments/{tenantId}` returns synthetic manual/unverified evaluations for one of those tenants.
- Synthetic routes require a loopback peer. Never expose a development preview through a proxy or public ingress.
- No authenticated real-service API is implemented; other routes fail closed. Production rejects preview-enabled configuration.

The web console currently loads its local synthetic fixtures independently; it is not a production API integration.

## Verification

From the repository root:

```powershell
dotnet test ./m365-azure-security-assessment-platform/M365.SecurityAssessment.slnx
dotnet build ./m365-azure-security-assessment-platform/M365.SecurityAssessment.slnx
npm run lint --prefix ./m365-azure-security-assessment-platform/web
npm run typecheck --prefix ./m365-azure-security-assessment-platform/web
npm test --prefix ./m365-azure-security-assessment-platform/web
```

With the synthetic dev server running, install Chromium once and run desktop/mobile journeys:

```powershell
npm exec --prefix ./m365-azure-security-assessment-platform/web -- playwright install chromium
npm run test:e2e --prefix ./m365-azure-security-assessment-platform/web
```

Browser tests cover navigation, tenant switching, filters, evidence dialogs, keyboard close/focus, audit outcomes, downloads, responsive widths and visible text overlap. The suite blocks non-loopback requests. Screenshots and results are ignored under `web/test-results/`.

Stop the development server before building production because both use the same `.next` directory:

```powershell
npm run build --prefix ./m365-azure-security-assessment-platform/web
npm start --prefix ./m365-azure-security-assessment-platform/web -- --port 3001
```

The production server must show the unconfigured blocked view, never synthetic tenant data. Unit/API/browser test success does not qualify live Microsoft connectors, durable storage, or remediation.

## Dependencies and Agent Roles

For new installations or dependency changes, resolve the latest stable Next.js and required packages, check compatibility, pin exact installed versions and commit the lockfile. `npm ci` reproduces the reviewed lockfile; it does not silently upgrade dependencies. Prereleases and silent downgrades are not substitutes for resolving compatibility.

Oxlint is the current frontend linter, replacing the ESLint/TypeScript-7-incompatible toolchain. TypeScript remains the type checker. Tests use Vitest, xUnit, ASP.NET Core's in-memory test server and Playwright.

Workspace agents are in `../.github/agents/`: builder, reviewer, security auditor, tester and read-only Zero Trust researcher. They use non-overlapping ownership and return evidence to the coordinator. Only accepted OpenSpec tasks are checked off; fixture features do not imply completion of later production tasks.

## Remaining Gates

The OpenSpec change `m365-azure-security-baseline-platform` remains the implementation contract. Before production use, complete source/version and operation qualification, Entra/customer authorization, tenant-isolated PostgreSQL/session/evidence/audit persistence, job execution, service adapters, independent security validation, and qualified remediation. Azure geography, networking, costs, operating limits, retention and any live test scopes require explicit approval. Customer-hosted workers and live Zero Trust Assessment integration are not present.