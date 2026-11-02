# Independent synthetic validation - 2026-11-02

## Final Foundation Result

PASS for the implemented local synthetic foundation, not for the full production platform. This section supersedes the earlier failing runs retained below.

- Backend: 51 tests passed, zero failed/skipped; solution build passed.
- Frontend: 59 tests passed across five files; lint, typecheck and production build passed.
- Browser: all six desktop/mobile Playwright cases passed after the product and test-helper repairs. Navigation, filtering, scoped dialogs, keyboard close/focus, audit values, JSON/CSV downloads and viewport/text layout assertions passed.
- Coordinator visually inspected the mobile service-coverage screenshot and desktop audit-detail screenshot. Screenshots are ignored test artifacts, not customer evidence.
- Full and production-only npm audits report zero vulnerabilities; `npm outdated` reports no outdated packages.
- Docker engine is available in Linux mode. No PostgreSQL/RLS implementation or test is claimed by this foundation result.
- Production HTTP verification on 127.0.0.1:3001, with the preview flag deliberately enabled: root HTTP 200 displays Workspace unavailable / Access blocked, no synthetic customer data, and `/api/v1/synthetic/fixtures` returns 404. A first helper misinterpreted byte-array HTML; the coordinator decoded UTF-8 and verified the actual response directly.

Repairs: Turbopack root now includes the shared baseline directory; native dialog setup survives development effect replay; table scroll containers position absolute accessibility labels so they cannot expand the mobile document; audit values are recursively redacted and missing sensitivity classification fails closed. Backend regressions cover strict enum/version handling, nested collections, organization/purpose bindings, customer-specific scope and expected-set reporting. The layout helper excludes inert background text while a modal is active and checks foreground content.

Authentication, durable database/session/evidence/audit storage, live Microsoft adapters, qualified controls and remediation remain incomplete. The foundation result does not qualify those capabilities. See the platform README and OpenSpec checklist for current acceptance boundaries.

## Historical Validation Runs

## Verdict

FAIL: latest canonical browser run started at 2026-11-02T09:08:24.048Z and finished in 70.226s:
3 passed, 3 failed, 0 skipped, 0 flaky (exit 1). All three desktop cases pass; all three mobile cases
fail. The supplied development server at http://127.0.0.1:3000 returns HTTP 200. This supersedes the
previous 0/6 browser result: detail dialogs now open, but mobile layout/pointer failures remain.

Text-overlap checks now walk only the active native `dialog:modal` when present, excluding its inert
background, not dialog content. Document/body width checks remain active, with additional dialog
horizontal-overflow and viewport-bound checks. Existing collapsed-details exclusions remain.
Findings checks initial close-button focus, three forward/backward Tab cycles allowing Chromium's
body-focus transition but rejecting focus outside the modal, then Escape and opener-focus restoration.
No product focus behavior was modified. Tenant, filter, empty-state, redaction and export assertions
were preserved. Only one test-code correction/run iteration was needed; no failed-fix loop was pursued
for the remaining product failures.
Reproduce from repository root with `npm --prefix m365-azure-security-assessment-platform/web run test:e2e`.

## Commands and results

Commands ran from repository root against the already-running dev server; no server was started,
stopped or restarted. Previous unit-suite results below are explicitly not fresh browser-run evidence.

| Command | Exit | Observed result |
| --- | --- | --- |
| `npm --prefix m365-azure-security-assessment-platform/web run test:e2e` | 1 | Fresh: 3 passed, 3 failed, 0 skipped, 0 flaky |
| `node --input-type=module -e '<local Playwright audit screenshot inspection>'` | 0 | Fresh: Node v26.12.0; HTTP 200 at both widths; keyboard-opened audit dialog and scrolled to bottom at 390 and 1440; diagnostic, not another passing test |
| `node -e '<parse test-results/results.json>'` | 0 | Fresh: extracted counts, errors, layout attachments and observations |
| `docker info --format 'OSType={{.OSType}} ServerVersion={{.ServerVersion}} Name={{.Name}}'` | 0 | Daemon responding; Linux, Docker 29.8.2, docker-desktop |
| `npm --prefix m365-azure-security-assessment-platform/web test` | 0, prior run | Previous: 5 files, 59 passed, 0 failed, 0 skipped; not rerun in this focused browser task |
| `dotnet test m365-azure-security-assessment-platform/M365.SecurityAssessment.slnx` | 0, prior run | Previous: 51 passed, 0 failed, 0 skipped; not rerun in this focused browser task |

Editor diagnostics: no errors in the two edited TypeScript files. Production build was NOT run while
dev remains active. No cloud/customer/external API calls, live contracts, credentials, container
creation, git operations or background polling. All browser requests were restricted to loopback.

## Browser coverage and artifacts

The three existing journey specs run in desktop (1440x1000) and mobile (390x844) projects. The suite
reuses the supplied server and blocks non-loopback requests. Browser observations reported zero
console/page errors and zero blocked external origins for all six cases.

| Project / case | Result and concrete behavior |
| --- | --- |
| Desktop / audit | PASS: six outcome distinctions, combined filters, detail, Escape, JSON/CSV parity, reversed-date empty export, redaction and customer-B isolation. |
| Desktop / findings | PASS: filters, keyboard open, initial focus, forward/backward focus containment, Escape/opener restoration, JSON/CSV exports, script-text empty search and customer-B isolation. |
| Desktop / navigation | PASS: five screens, layout checks, detail open/close and customer-switch state reset. |
| Mobile / audit | FAIL: 30s timeout at `audit.spec.ts:21`, clicking the single filtered `tbody .row-link`; sidebar/workspace intercept pointer events. Detail/export/redaction assertions after this step were not reached. |
| Mobile / findings | FAIL: `finding-detail` expected viewport/document <=390, received 617 for both. Dialog opens; focus cycles, Escape restoration, exports, negative search and tenant isolation continue and pass (only the two layout soft assertions fail). |
| Mobile / navigation | FAIL: Assessments 594px, Findings 619px and Audit 956px viewport/document width versus configured 390px; Overview and Coverage both 390px. Then `navigation.spec.ts:14` audit-row click times out with sidebar/workspace interception; subsequent tenant reset is unverified. |

### Actual screenshot/layout inspection

Desktop: all five screen captures measure viewport/document width 1440, with zero detected visible-text
overlaps. Findings table columns and audit outcomes are aligned and readable. Both right-side drawers
occupy x=900..1440, y=0..1000, with client/scroll width 539px and zero detected text overlap. Audit
scrolls vertically: 1374px content in 1000px client height, reaching scrollTop 374. Top and bottom
screenshots show readable JSON, actor/correlation fields and final notice without text collisions.

Mobile: full-page captures expose extra blank width and horizontally clipped table columns; no
detected visible-text collisions, but this is NOT a passing no-overflow result. The findings drawer is
390px wide at x=227..617, y=0..844; client/scroll width 389px. Its content is readable in the expanded
capture, while the page viewport/document are incorrectly 617px wide. The audit table measures
1000px in a 358px `overflow-x:auto` container; the desktop equivalent is 1152px in 1152px.

Supplemental keyboard-only audit inspection at configured 390px measured layout viewport/document
951px and visual viewport 390px; drawer x=561..951, y=0..844, client/scroll width 389px. Keyboard
activation auto-scrolls the visual viewport to show the drawer; the 390px screenshots show readable
top and bottom content, not a blank dialog. Audit vertical scrolling works: content 1399px/client
844px, reaching scrollTop 555. This does not waive the canonical pointer timeout or inflated layout
viewport. No forced clicks, injected CSS, product focus fixes or weakened width assertions were used.

Final ignored artifacts under `web/test-results/`:

- `results.json`: final exact counts, observations and per-test errors.
- Desktop full-page screenshots: `navigation-five-screens-re-5b09f--tenant-switch-resets-state-desktop/{overview,assessments,findings,coverage,audit}.png`.
- Mobile full-page screenshots: `navigation-five-screens-re-5b09f--tenant-switch-resets-state-mobile/{overview,assessments,findings,coverage,audit}.png`.
- Both findings project directories: `finding-detail.png`; desktop audit directory: `audit-detail.png`.
- Supplemental `inspection-{390,1440}-audit-{table,dialog,dialog-bottom}.png`: actual viewport captures, visually inspected.
- Each of the three failed mobile directories: `test-failed-1.png`, `error-context.md`, `trace.zip`.

Desktop and mobile screenshots were visually inspected. Test artifacts are ignored by the existing
`**/test-results/` rule; no binary was added as a source file.

## Coverage boundaries

Authentication, authorized tenant assignment, encrypted sessions, durable audit, PostgreSQL/RLS,
queue execution, live adapters, source-qualified controls, remediation and protected server downloads
remain outside this synthetic browser slice. Export/customer checks prove fixture UI scoping, not
server authorization. Production build, frontend unit and backend suites were not rerun in this task.
No OpenSpec task was marked complete. Docker is available but real-service integration is UNVERIFIED;
no infrastructure substitute was represented as production success.

```text
environment: Docker AVAILABLE (daemon 29.8.2/linux); Node AVAILABLE (v26.12.0); Playwright AVAILABLE (Chromium ran)
startup: PASS - existing dev server, browser GET / returned HTTP 200; no new startup timing measured
integration: UNVERIFIED - exit_code: n/a; not rerun; previous frontend 59/backend 51 passes only
e2e: FAIL - exit_code: 1; passed: 3, failed: 3, skipped: 0, flaky: 0
overall: FAIL - mobile viewport expansion and audit-row pointer interception; desktop slice passes
```

## Activity handoff for coordinator's root development log

2026-11-02: Read root/web instructions, runtime-validation skill, assigned task checklist, existing
E2E helpers/findings/audit/config/package/report. Corrected active-modal text scope and native focus
assertions in `tests/e2e/helpers.ts` and `tests/e2e/findings.spec.ts`; immediately ran canonical E2E
(3 pass/3 fail/0 skip). Parsed JSON evidence, checked Docker daemon and editor diagnostics, captured
keyboard-only audit top/bottom/table screenshots at 390/1440, and visually inspected dialogs/tables.
Updated this report. No product, root log, OpenSpec, git, package or shared documentation edits;
coordinator owns the root log entry. Dev remains running; no production build or cloud/customer writes.