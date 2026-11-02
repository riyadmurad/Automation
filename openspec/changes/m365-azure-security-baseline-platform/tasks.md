# Tasks

This checklist is for a later, explicitly authorized implementation workflow. All tasks start incomplete. Planning does not authorize deployment, customer consent changes, or live tests. See [design.md](design.md) for architecture and the eight capability specifications for acceptance scenarios. Approved deployment geography, sizing, budget and retention values are required before customer activation, not assumptions to embed in code.

## Delivery Gates and Global Dependencies

- G0 Foundation: local project layout, typed contracts, tenant/session/credential partitions, audit, and synthetic fixtures.
- G1 Control acceptance: source-revision-pinned 13-control inventory and passing normalized fixture suite.
- G2 Read-only service evidence: only verified supported operations; every remaining control stays manual, unknown, or unsupported.
- G3 Operation feasibility: read, conditional-write/race protection, post-write verification, and recovery proven before broad remediation or remediation UI.
- G4 Narrow writes: one-at-a-time allowlist qualification with exact approval and disposable-scope evidence.
- G5 Release/pilot: regression, immutable image digest, migration/recovery, cost/network, production SLO, and customer approval gates.

Global dependencies: no live tests without explicit authorization for named disposable test tenants/resources; no infrastructure deployment before region, VNet/private-DNS/outbound profile, Service Bus Premium cost/capacity, and production operational thresholds are explicitly approved; no customer activation before retention, backup, residency and deletion policies are approved. Proposed development/test values in design.md are not production SLOs.

## 1. Application Foundation

- [x] 1.1 Create `m365-azure-security-assessment-platform/` with Next.js/TypeScript web, ASP.NET Core API, worker projects and test harnesses; pin supported stable runtime versions and verify frontend build/typecheck and backend build pass without modifying existing utility scripts.
- [x] 1.2 Establish versioned API, evidence, adapter, job and operation contracts with synthetic two-customer fixtures; verify contract validation rejects unknown or incompatible versions and excludes credential fields from browser models.
- [x] 1.3 Freeze the 13 stable control IDs ENT-001, EXO-001, TEAM-001, SPO-001, PP-001, INT-001, MDI-001, MDO-001, MDE-001, MDCA-001, XDR-001, PUR-001 and AZR-001 from design section 6, including source revision/hash, normalized fixture fields, exact expected state, support mode, permissions, license/runtime and evidence; verify each ID maps to one spec scenario, named fixtures and a task, with no unverified API claimed as supported.
- [x] 1.4 Document local setup, synthetic test identities and safe fixture execution; verify documented build/test commands work without production credentials or network writes to customer services.
- [x] 1.5 Add normalized fixture validators for the 13 initial controls and all required evidence fields; verify compliant, noncompliant, denied, partial, stale, unsupported, missing-license and manual cases produce distinct expected outcomes.

## 2. Authentication and Customer Authorization

- [ ] 2.1 Implement the Next.js server-side OIDC authorization-code/PKCE session flow and API-audience token forwarding; verify nonce/state, expiry, logout, CSRF/origin protection and Secure/HttpOnly/SameSite cookies, with no downstream tokens in browser storage.
- [ ] 2.2 Implement Microsoft.Identity.Web API validation and organization/customer assignment lookup; verify wrong issuer/audience/tenant, expired tokens and signed-in unassigned users are rejected.
- [ ] 2.3 Implement Viewer, Assessor, Approver, Operator and administration policies with independent/customer approval configuration; verify role boundaries, self-approval denial and direct API bypass attempts.
- [ ] 2.4 Implement separate assessment/remediation connection onboarding, permission-health checks and credential references; verify read consent cannot authorize writes and missing permissions are explained without exposing credentials.
- [ ] 2.5 Enforce standalone tenant binding and operator/connection revocation at admission; verify second-tenant requests and revoked assignments are denied even with otherwise valid tokens.
- [ ] 2.6 Document operator roles, consent requirements and credential rotation; verify rotation preserves tenant ownership and connection failures are redacted.
- [ ] 2.7 Implement encrypted PostgreSQL session/MSAL cache persistence partitioned by immutable user and tenant IDs, with key version, TTL, revocation and Key Vault-protected envelope keys; verify cross-instance failover, cross-tenant denial, key rotation/re-encryption, unavailable-key fail-closed behavior and restore without session resurrection.

## 3. Tenant-Safe Metadata and Evidence Storage

- [ ] 3.1 Implement PostgreSQL ownership columns, composite constraints, migrations and non-owner application roles with row-level security, including user/tenant session partitions; verify two-customer access denial and transaction-local tenant context isolation under connection pooling.
- [ ] 3.2 Implement immutable versioned metadata for profiles, assessments, findings, exceptions, emergency registries, plans, approvals and operation state; verify cross-customer references fail and published/history records cannot be silently overwritten.
- [ ] 3.3 Implement scoped evidence/report blob ingestion and retrieval with integrity metadata and field redaction; verify missing/tampered evidence is identified, protected downloads are reauthorized and frontend identities cannot fetch secrets or unrelated objects.
- [ ] 3.4 Document evidence minimization and metadata migration behavior; verify fixtures preserve null, absent, unknown and redacted values distinctly and collect no document/mailbox contents or emergency credentials.

## 4. Setting-Level Change Audit Ledger

- [ ] 4.1 Implement the structured event schema with UTC timestamp, customer/organization, authorized user, requester/approver/executor, M365/Azure service, resource, setting name, previous/requested/verified values, outcome and correlation IDs; verify schema tests require these fields or explicit unavailable/redacted markers.
- [ ] 4.2 Implement trusted actor attribution from validated tokens, stored authorization and approval context; verify spoofed user names are ignored, user and workload identities remain distinct, and scheduled/external/unknown actors are not falsely attributed.
- [ ] 4.3 Implement append-only intent/result/verification persistence with restricted update/delete rights and integrity/archive metadata; verify earlier events cannot be edited and simulated audit failure prevents the customer write from starting.
- [ ] 4.4 Implement secret redaction before storage and at rendering/export boundaries; verify passwords, access tokens, private keys and recovery codes never appear in audit records, exceptions, telemetry or exported old/new values.
- [ ] 4.5 Implement tenant-authorized audit query and JSON/CSV export endpoints with time/user/service/setting/resource/outcome filters; verify export parity, cross-customer denial, and retention enforcement.
- [ ] 4.6 Document audit field meanings and intent/unknown/verified event examples; verify a synthetic multi-setting change preserves per-setting before/after values and appends reconciliation instead of rewriting history.

## 5. Baseline Catalog and Effective Profiles

- [ ] 5.1 Implement declarative pack schemas, provenance/integrity validation, immutable releases and Microsoft/CISA/CIS/SANS source classifications; verify uncited, executable or rights-unapproved content cannot activate.
- [ ] 5.2 Implement source-update monitoring with retrieval/review freshness and reviewable diffs; verify a detected update does not mutate an active profile or authorize changes.
- [ ] 5.3 Implement platform-to-MSP-to-customer profile resolution and recorded conflict decisions; verify override provenance and unresolved incompatible settings block the affected plan.
- [ ] 5.4 Implement applicability/license/permission/cloud evaluation and version pinning; verify unavailable evidence is not not-applicable/compliant and historical assessments retain their original expected values.
- [ ] 5.5 Document baseline publishing, rights review and profile activation; verify a sample new release passes validation without overwriting the prior release or duplicating restricted benchmark text.

## 6. Queues and Isolated Execution

- [ ] 6.1 Implement Service Bus job envelopes and the transactional outbox; verify committed jobs survive dispatcher failure and duplicate delivery cannot duplicate operation admission.
- [ ] 6.2 Implement controller admission against stored tenant/job/connection ownership and customer-specific single-tenant assessment/remediation app-registration bindings; verify tenant mismatch, purpose mismatch, altered queue ownership, stale connection and credential-reference substitution are rejected before credential retrieval.
- [ ] 6.3 Implement per-customer assessment/remediation identity and credential partitions with isolated token caches; verify customer A cannot access customer B credentials and assessment workers cannot acquire write credentials.
- [ ] 6.4 Implement bounded retry, Retry-After handling, tenant concurrency quotas, dead-letter diagnostics and cancellation; verify one throttled customer does not starve another and jobs expose incomplete/error states.
- [ ] 6.5 Implement compatible-runtime routing for Windows-only adapters; verify Windows-required jobs block rather than fall through to Linux and route only to a separately isolated Windows VM job host.
- [ ] 6.6 Complete the early operation-feasibility gate for every proposed write: document source revision, read/scope/permission evidence, conditional-write or equivalent race protection, verification, propagation window and recovery; verify any unproven operation remains guidance-only before broad remediation or UI work begins.
- [ ] 6.7 Add an explicit authorization record and opt-in switch for live contract tests against named disposable tenants/resources; verify CI and default test runs cannot connect to customer tenants and missing/expired authorization blocks before connection.
- [ ] 6.8 Provision a separately isolated Windows VM job host only when an enabled adapter manifest requires it; verify network isolation, managed identity scope and startup health with synthetic jobs, or verify no capacity is provisioned when no manifest requires Windows.
- [ ] 6.9 Define and exercise the Windows job-host patch and image-refresh process; verify supported module versions, security updates, rollback, and workload drain without exposing customer credentials.
- [ ] 6.10 Implement tenant/purpose-bound Windows job dispatch and per-job cleanup/VM teardown; verify credentials and temporary evidence are removed after success, cancellation, timeout and worker failure.

## 7. Assessment Engine and Adapter Harness

- [ ] 7.1 Implement adapter manifests, paginated collection, typed evidence normalization and read-only operation enforcement; verify fixtures cover success, denied access, missing license, partial pagination, unsupported cloud and accidental write attempts.
- [ ] 7.2 Implement six-status control evaluation, effective-scope evidence and coverage counts; verify policy existence alone cannot pass an unassigned control and absent expected inventory cannot yield fabricated device coverage.
- [ ] 7.3 Implement on-demand/scheduled assessments and drift comparison with pinned profile/evaluator versions; verify configuration drift is distinguished from changed expected values and revoked connections stop jobs.
- [ ] 7.4 Document the adapter contract, supported-service matrix and fixture authoring; verify each enabled control has source, evidence, evaluation and limitation metadata and unsupported controls remain visible.
- [ ] 7.5 Implement bounded, resumable evidence batches with stable idempotency keys, checkpoints and per-batch source/version manifests; verify a partial, duplicate or interrupted batch cannot be reported as complete coverage.

## 8. Entra and Collaboration Assessments

- [ ] 8.1 Implement ENT-001 Entra authentication/consent/privileged-access checks; verify the `control_ENT-001_*` compliant, noncompliant, denied and unknown-scope fixtures and publish operation-specific permission notes.
- [ ] 8.2 Implement supported Conditional Access inclusion/exclusion and overlapping-scope evaluation; verify unresolved policy evidence yields unable-to-assess rather than an inferred safe result and document analyzer limitations.
- [ ] 8.3 Implement EXO-001 Exchange external-forwarding checks from the matrix; verify exact false expected state, complete organization/recipient scope fixtures and document support only after operation verification.
- [ ] 8.4 Implement TEAM-001 Teams anonymous-join policy collection and evaluation; verify the exact false expected state and effective-assignment fixtures, preserving its distinction from SharePoint controls.
- [ ] 8.5 Implement Teams meeting/application policy assignment checks; verify effective scope and precedence fixtures, including unsupported or incomplete assignment evidence.
- [ ] 8.6 Implement SPO-001 SharePoint/OneDrive default-link checks; verify exact `organization` audience and `view` permission fixtures, site scope, and that tenant defaults do not prove site-level coverage.
- [ ] 8.7 Implement SharePoint/OneDrive default-link setting evidence and finding previews; verify actual/expected values and new-link versus existing-link impact remain distinguishable in reports.

## 9. Power Platform, Intune and Purview Assessments

- [ ] 9.1 Implement PP-001 Power Platform/Power Automate environment and DLP checks; verify all expected environments are covered, DLP is enabled and each prohibited connector-pair fixture is blocked; document support only after operation verification.
- [ ] 9.2 Implement supported Power Automate flow ownership/sharing checks and manual-evidence fallback; verify flow inventory alone never establishes governance compliance and update the coverage matrix.
- [ ] 9.3 Implement INT-001 Intune effective deployment/compliance checks; verify exact expected-device equality, empty pending/failed/unassigned sets and inventory provenance using fixtures.
- [ ] 9.4 Add Intune effective deployment/coverage reports and permission notes; verify a created but unassigned profile does not pass and unknown inventory is disclosed.
- [ ] 9.5 Implement PUR-001 Purview unified-audit-log checks and supported DLP scope checks; verify enabled/fresh expected state, uncovered locations and unavailable licenses remain visible and document support status.
- [ ] 9.6 Implement the matrix's information-protection/retention evidence checks with manual review where required; verify no destructive retention write is enabled and findings preserve legal/retention context.

## 10. Defender Product and XDR Assessments

- [ ] 10.1 Implement MDI-001 Defender for Identity sensor-health/coverage checks; verify healthy sensors equal the expected domain-controller inventory and inaccessible evidence yields manual/unknown status.
- [ ] 10.2 Implement MDO-001 Defender for Office 365 Safe Links checks; verify enabled=true, click-through=false, exact expected-recipient coverage, policy precedence and license limitations.
- [ ] 10.3 Implement MDE-001 Defender for Endpoint tamper-protection checks plus onboarding/ASR/EDR evidence; verify protected devices equal expected inventory and unavailable signals stay unknown.
- [ ] 10.4 Implement MDCA-001 Defender for Cloud Apps governance coverage checks; verify expected apps are fully covered by enabled policies and app connection alone never establishes policy coverage.
- [ ] 10.5 Implement XDR-001 Defender XDR integration checks; verify integrated licensed-product IDs equal expected IDs, unknown IDs remain visible and incidents are not treated as configuration evidence.
- [ ] 10.6 Publish product-specific control/permission notes and canonical finding mappings; verify fixture runs do not double-count the same control across Defender products/frameworks.

## 11. Azure Assessments

- [ ] 11.1 Implement AZR-001 Azure storage secure-transfer checks and scoped ARM evidence; verify secure-transfer-required IDs equal the complete expected inventory and subscription/resource-group boundaries remain explicit.
- [ ] 11.2 Implement Azure Policy and Defender for Cloud recommendation/compliance enrichment; verify favorable scores do not override failed direct checks and document data-plane/delegation limitations.

## 12. Emergency-Access Registry and Safety Checks

- [ ] 12.1 Implement approved emergency-account registry revisions keyed by immutable object ID, owner, exclusions and readiness review; verify renames preserve protection while name-pattern matches alone do not add exclusions.
- [ ] 12.2 Implement fresh enabled-state/recovery-access/effective-policy preflight; verify deleted, disabled, wrong-tenant, stale-review and unresolved-overlap cases block lockout-sensitive enforcement.
- [ ] 12.3 Implement protected-property denial and registry-change plan invalidation; verify ordinary bulk remediation cannot disable/delete/change emergency authentication and approval is invalidated on registry edits.
- [ ] 12.4 Implement emergency-account exception and monitoring/readiness views with customer-run test metadata; verify excluded accounts remain assessed, unavailable telemetry is unknown and no emergency credentials are requested or stored; document manual recovery exercises.

## 13. Lighthouse and Partner Connections

- [ ] 13.1 Implement independent GDAP, Microsoft 365 Lighthouse, Azure Lighthouse and direct-consent connection state; verify Azure delegation alone cannot authorize M365 access and CSP status alone cannot establish customer eligibility.
- [ ] 13.2 Implement current documented Lighthouse eligibility checks and supported portal navigation; verify limited/ineligible customers retain direct connectors and returning from a portal does not mark a finding fixed.
- [ ] 13.3 Implement supported Azure Lighthouse scope/role preflight and relationship-expiry handling; verify unsupported operations block without expanding grants or silently switching credentials.
- [ ] 13.4 Implement an isolated default-disabled Lighthouse preview evaluation switch and production rejection policy; verify delegated-only/beta capabilities cannot feed unattended production findings or writes, and document authentication/cloud/eligibility limitations.

## 14. Findings, Exceptions and Report Generation

- [ ] 14.1 Implement detailed finding/evidence APIs and a common immutable report model; verify actual/expected values, source revisions, targets, impact, proposed changes, limitations and timestamps are present.
- [ ] 14.2 Implement scoped expiring risk exceptions with owner, approver, rationale and compensating controls; verify expiry restores actionability without changing measured history or counting accepted risk as compliance.
- [ ] 14.3 Implement honest status/coverage summaries and assessment history comparisons; verify unknown/manual/unsupported controls and score denominators remain visible and no output claims regulatory certification.
- [ ] 14.4 Implement HTML/PDF and JSON/CSV generation in a bounded report worker; verify cross-format parity, historical reproducibility, HTML/CSV injection defense and renderer denial of external/file access.
- [ ] 14.5 Implement authorized generation/download lifecycle and links to change-audit evidence; verify revoked users and cross-customer identifiers cannot retrieve reports and document evidence freshness/retention semantics.

## 15. Guarded Remediation and Recovery

- [ ] 15.1 Implement immutable plan revisions/hashes containing targets, versions, before/after values, relevant fingerprints, emergency registry and expiry; verify edited plans invalidate approvals and cross-customer bulk plans are rejected.
- [ ] 15.2 Implement exact-plan independent/customer approval and execution admission; verify stale/expired approvals, missing permissions, self-approval and changed authorization block execution.
- [ ] 15.3 Implement fresh per-operation setting/scope/permission/emergency/audit/stop preflight; verify configuration drift and missing safety evidence fail closed before writes.
- [ ] 15.4 Implement resource leases, durable operation IDs and supported conditional writes; verify duplicate delivery and external administrator races do not cause repeated or clobbering changes.
- [ ] 15.5 Implement durable setting-level audit intent and result integration; verify every attempted setting change records the requested fields, a lost response stays unknown and missing audit persistence prevents writes.
- [ ] 15.6 Implement partial failure, read-before-retry reconciliation and bounded post-change verification; verify accepted-but-not-effective changes remain pending/unverified and earlier successful operations are not erased by later failures.
- [ ] 15.7 Implement global/customer stop flags, safe cancellation and separate recovery plans; verify no new writes start after stop and intervening customer edits prevent snapshot restoration.
- [ ] 15.8 After task 6.6 passes, qualify one reviewed reversible candidate at a time; run any live test only under task 6.7 authorization for named disposable scope, and verify conditional-write, impact, verification and recovery evidence before enabling it while all unqualified/high-risk operations remain guidance-only.
- [ ] 15.9 Document operation prerequisites, unsupported recovery and lockout-sensitive report-only/pilot stages; verify each enabled operation has evidence-backed authorization, safety, verification and recovery tests rather than a generic script executor.

## 16. Next.js Operator Workspaces

- [ ] 16.1 Implement the scoped MSP overview, customer selection and persistent customer name/tenant ID; verify assigned-customer visibility, standalone restrictions and no shared-cache leakage across users or tenants.
- [ ] 16.2 Implement Connections/Coverage/Assessments/Findings views with filterable tables and current-versus-expected evidence; verify missing-license, denied, unsupported and partial states are distinguishable in browser tests.
- [ ] 16.3 Implement change preview, approval and execution views with target scope, safety exclusions, impact and verification states; verify buttons cannot bypass API roles and no generic cross-customer fix-all action exists.
- [ ] 16.4 Implement exception, emergency-readiness and report workflows; verify expiry, manual evidence and protected-account reasons are readable without exposing sensitive data.
- [ ] 16.5 Implement the Audit view with UTC timestamp, authorized user, service, setting, previous/new/verified values and filters/export; verify real fixture event chains, redacted values and failed/unknown states remain distinct.
- [ ] 16.6 Add responsive, accessible states and contextual operator documentation for these workflows; verify keyboard operation, loading/error/empty states, desktop/mobile layout and protected downloads through browser tests and screenshots.

## 17. Hosting, Lifecycle and Recovery Controls

- [ ] 17.1 Author parameterized central/standalone Azure infrastructure definitions for Container Apps, PostgreSQL, Blob Storage, Key Vault and monitoring; verify static validation, encryption, managed-identity roles and separate tenant/purpose credential grants without deploying resources.
- [ ] 17.2 Implement activation gates for approved geography, retention, backup policy, production SLOs, scale, network profile and budget inputs; verify missing approvals prevent production provisioning/activation while synthetic development/test defaults remain usable.
- [ ] 17.3 Implement redacted operational telemetry, audit archival and security alerts; verify correlation survives worker/report failures, secrets never appear in telemetry and normal app roles cannot rewrite audit history.
- [ ] 17.4 Implement offboarding and legal-hold-aware deletion/retention; verify queued writes stop, connection access is disabled and the report distinguishes remaining external grants, retained records and backup expiry.
- [ ] 17.5 Implement restore quarantine, schema compatibility and application rollback procedures; verify restored revoked access/jobs stay disabled, incompatible workers reject new contracts and rollback preserves customer evidence/audit without reversing settings.
- [ ] 17.6 Document safe deployment, cost/region review, backup restoration and incident-stop procedures; verify an execution-disabled recovery drill and infrastructure checks before requesting any separately authorized pilot deployment.
- [ ] 17.7 Encode proposed development/test operational defaults from design.md, including active-job limit, job timeout, retry/backoff, queue-age alert, uncertain-write reconciliation, fixture retention, budget alerts and recovery test targets; verify defaults are measurable and cannot be inherited as production SLOs.
- [ ] 17.8 Define pre-production/production VNet integration, private DNS for private endpoints, controlled outbound routing to required Microsoft APIs and network-failure behavior; verify DNS/egress policy statically and require explicit network-profile approval before provisioning.
- [ ] 17.9 Require Service Bus Premium for production and produce a capacity/cost estimate tied to the approved workload profile; verify deployment validation rejects another tier and blocks absent explicit cost approval without creating resources.

## 18. Cross-Capability Acceptance

- [ ] 18.1 Run the full synthetic two-customer and standalone scenario suites; verify tenant isolation across API, database pools, caches, credentials, queues, evidence, audit and report downloads, and retain test evidence mapped to all eight specifications.
- [ ] 18.2 Run assessment-to-report-to-approved-change-to-verification integration tests with drift, revocation, emergency-account overlap, audit outage, duplicate delivery, uncertain write and partial failure injection; verify exact audit fields and safe final states throughout.
- [ ] 18.3 Run module coverage and dependency/security checks for every declared service; verify unsupported gaps stay visible, no placeholder module is called complete, and all enabled baseline controls have source/evidence tests and operation-specific permission documentation.
- [ ] 18.4 Execute supported live contract tests only after explicit authorization for named disposable test tenants/resources; verify no production customer is a default target, capture untested license/API limitations and keep affected write capabilities disabled until qualified.
- [ ] 18.5 Build immutable image digests with provenance and run all eight capability regression suites in CI; verify promotion references the tested digest and rejects mutable tags.
- [ ] 18.6 Run schema/job compatibility and database migration tests against the prior supported release; verify expand/contract compatibility, safe rollback, and rejection of incompatible worker/job contracts before promotion.
- [ ] 18.7 Assemble a bounded milestone evidence bundle; verify source revisions, test identifiers/results, immutable image digest, migration compatibility, approvals, unresolved gaps and stop conditions are present before promotion.

## Capability Traceability

| Specification                                              | Primary Task Groups    |
| ---------------------------------------------------------- | ---------------------- |
| [tenant-access](specs/tenant-access/spec.md)               | 2, 3, 6, 16, 18        |
| [baseline-catalog](specs/baseline-catalog/spec.md)         | 1, 5, 7, 18            |
| [service-assessment](specs/service-assessment/spec.md)     | 6, 7, 8, 9, 10, 11, 18 |
| [evidence-reporting](specs/evidence-reporting/spec.md)     | 3, 4, 14, 16, 18       |
| [safe-remediation](specs/safe-remediation/spec.md)         | 4, 6, 12, 15, 18       |
| [emergency-access](specs/emergency-access/spec.md)         | 8, 12, 15, 16, 18      |
| [partner-integrations](specs/partner-integrations/spec.md) | 2, 11, 13, 18          |
| [platform-operations](specs/platform-operations/spec.md)   | 3, 4, 6, 17, 18        |

## Initial Control Traceability

| Control ID | Assessment scenario in `service-assessment/spec.md`       | Implementation task | Fixture test family       |
| ---------- | --------------------------------------------------------- | ------------------- | ------------------------- |
| ENT-001    | ENT-001 privileged roles are covered                      | 8.1                 | `control_ENT-001_<case>`  |
| EXO-001    | EXO-001 external forwarding is disabled                   | 8.3                 | `control_EXO-001_<case>`  |
| TEAM-001   | TEAM-001 anonymous meeting join is disabled               | 8.4                 | `control_TEAM-001_<case>` |
| SPO-001    | SPO-001 default links use organization view access        | 8.6                 | `control_SPO-001_<case>`  |
| PP-001     | PP-001 DLP covers expected environments                   | 9.1                 | `control_PP-001_<case>`   |
| INT-001    | INT-001 expected devices are compliant                    | 9.3                 | `control_INT-001_<case>`  |
| MDI-001    | MDI-001 expected domain controllers have healthy sensors  | 10.1                | `control_MDI-001_<case>`  |
| MDO-001    | MDO-001 Safe Links covers expected recipients             | 10.2                | `control_MDO-001_<case>`  |
| MDE-001    | MDE-001 expected devices have tamper protection           | 10.3                | `control_MDE-001_<case>`  |
| MDCA-001   | MDCA-001 governance policies cover expected apps          | 10.4                | `control_MDCA-001_<case>` |
| XDR-001    | XDR-001 expected licensed products are integrated         | 10.5                | `control_XDR-001_<case>`  |
| PUR-001    | PUR-001 unified audit logging is enabled                  | 9.5                 | `control_PUR-001_<case>`  |
| AZR-001    | AZR-001 expected storage accounts require secure transfer | 11.1                | `control_AZR-001_<case>`  |

## Workflow follow-up

- Review these planning artifacts before explicitly starting implementation with `/opsx-apply` or an equivalent request.
- Resolve deployment parameter approvals and authorize any live tests/deployment separately; planning and fixture tests do not grant customer access.
- Archive this change only after implementation tasks and review gates are satisfied; retain reported coverage gaps rather than treating archive as certification.