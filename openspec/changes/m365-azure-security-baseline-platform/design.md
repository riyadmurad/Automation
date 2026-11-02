# Design

## Context

See [proposal.md](proposal.md) for motivation and the eight capability specifications under `specs/` for behavior. The inspected repository contains independent PowerShell lab and knowledge-management utilities, no application runtime, and no existing durable OpenSpec capabilities. Those utilities are not production security defaults and will not be modified or imported into workers.

Confirmed decisions are MSP multi-customer and standalone operation, central MSP Azure hosting, CSP indirect-reseller integration, comprehensive Microsoft 365 and Azure assessment scope, detailed reports, limited approved remediation, and emergency-access safeguards. The user selected Next.js for the interface after discussing the stack. This design adopts the proposed .NET backend and Azure managed services; it creates no resources.

## Goals / Non-Goals

**Goals:**
- Establish a tenant-safe modular application with stable adapter and evidence contracts.
- Separate assessment coverage from permission to write, and make gaps and uncertainty observable.
- Preserve reproducible assessments and approvals across baseline, adapter, and customer configuration changes.
- Support one codebase for centrally hosted MSP and single-customer deployments.

**Non-Goals:**
- Building application code or infrastructure during this planning workflow.
- Universal automation of every Microsoft setting, regulatory certification, or automatic enforcement of newly published recommendations.
- Customer-hosted workers, cross-customer bulk changes, a public plugin marketplace, or arbitrary uploaded remediation scripts in the initial release.
- Replacing Microsoft portals, SIEM, incident response, or vendor-native protection engines. Incidents and Secure Score are context, not substitutes for configuration evidence.

## Decisions

### 1. Next.js frontend and authoritative .NET API

Use Next.js App Router with TypeScript and React for the console, ASP.NET Core on a supported .NET LTS release for the API, and separately deployed workers. Pin supported stable framework and SDK versions when implementing and record compatibility; do not hardcode a speculative future version in this plan.

Next.js owns routing, rendering and a thin server-side session/BFF layer. The .NET API owns organization/customer authorization, control evaluation, job admission, approval and operation state. Assessment and remediation never execute inside a Next.js request. Neither server components nor route handlers access customer credentials or the database directly.

Use supported identity libraries: MSAL Node for the frontend server's OIDC authorization-code/PKCE flow and Microsoft.Identity.Web for API access-token validation and supported delegated downstream flows. The browser receives a Secure, HttpOnly, SameSite session cookie, not Graph/ARM tokens or a refresh token. Persist session and MSAL cache state in PostgreSQL so frontend replicas survive instance loss; partition by immutable user and tenant identifiers, enforce tenant ownership, encrypt cache payloads with envelope keys protected by Key Vault, and store key version/expiry metadata. Rotation re-encrypts or safely invalidates records; revoked, expired, undecryptable, or stale-key records fail closed. Define backup/restore and key-recovery behavior without reviving revoked sessions. State-changing BFF endpoints enforce CSRF and origin checks. The BFF calls the API with a token for the API's audience, never an ID token. Platform assignments are looked up server-side rather than trusting a selected tenant or email domain. Require MFA through the deployment's Entra access policy.

Disable shared caching/static generation of authenticated customer content. Any permitted server cache is keyed by organization, tenant, principal authorization context, assessment and version; downloads are reauthorized at retrieval. Apply CSP and normal output encoding, and sanitize evidence-derived content.

Alternative: a React SPA is simpler to host but does not provide the selected Next.js server frontend. An all-Next.js backend reduces runtimes but puts privileged orchestration and service adapters in the UI runtime. The two-runtime cost is accepted to keep the security boundary explicit.

### 2. Modular monolith plus isolated execution

Organize the .NET API by the eight capability boundaries rather than creating a microservice per service family. Use versioned contracts for jobs, adapters, evidence and plans. Assessment, remediation, reporting, and source-update work have separate execution roles.

```text
Browser --> Next.js session/BFF --> .NET API + authorization
                                      |
                         PostgreSQL + durable outbox
                                      |
                               Service Bus
                                      |
                            Execution controller
                              |             |
                       Assessment jobs  Remediation jobs
                              |             |
                           Customer service APIs
                              |
                     Evidence ingestion --> Blob Storage

API --> scoped report jobs --> protected report downloads
```

Use Azure Container Apps for the web and API and Container Apps Jobs for compatible bounded Linux work. If an enabled adapter manifest requires Windows, run it on an isolated Windows VM job host in the MSP environment, never as an assumed Container Apps workload or Linux image. Provision only when a verified enabled manifest requires it; tasks cover provisioning, patching, credential isolation, teardown, and cleanup. ScubaGear is an optional assessment adapter, never a prerequisite for all service coverage or an automatic remediator.

Proposed implementation area is `m365-azure-security-assessment-platform/`, with `web/`, `api/`, `workers/`, `baselines/`, `tests/`, and `infra/`. This is a future layout, not a scaffold to create during planning. Avoid shared client models that expose credential or internal approval fields to the browser.

Alternative: many microservices introduce distributed consistency overhead before it is justified. A single shared process with all customer credentials offers insufficient execution isolation.

### 3. Tenant and credential partitioning

Model management organization and customer tenant as separate identities with explicit operator assignments. Store `organization_id` and `customer_tenant_id` on customer-owned rows and use composite relationships to prevent cross-tenant references. Enforce API ownership filters plus PostgreSQL row-level security using a non-owner application role and transaction-local tenant context; test pooled connections for context leakage.

Keep platform-administrator access distinct from customer-remediation authorization. The standalone binding is enforced in API admission and worker execution, not just onboarding UI.

Use managed identities for platform services. Customer workload access uses supported application consent, delegated authorization, federation, or certificates as appropriate per operation; a platform managed identity does not magically grant Graph access in another tenant. Prefer certificate/federation over shared client secrets, and do not assume GDAP grants app-only permission.

Provision customer-specific single-tenant connector app registrations per customer and purpose: separate assessment and remediation registrations. Do not use shared multitenant connector signing credentials for customer execution or deliver such credentials to workers. Store each customer's purpose-bound connector credential in its protected partition; a worker retrieves only the exact customer/purpose credential for its preconfigured job. Assessment workers cannot retrieve remediation credentials. Execution workers cannot list other customers' credentials or reuse a cross-customer token cache. Queue messages carry job references, ownership and contract versions, not secrets, arbitrary URLs, or executable scripts.

The execution controller validates the stored job and starts an approved preconfigured tenant/purpose job definition. Job identity, credential partition, single-tenant connector registration, purpose, and stored tenant must agree. Test negative binding cases for tenant mismatch, purpose mismatch, altered queue ownership, stale connection, and credential reference substitution before credential retrieval. Worker result/evidence endpoints validate the worker identity and job binding; they do not accept a tenant claim alone. Limit controller rights and treat it as a high-trust administrative component. Windows workers require the same tenant/purpose isolation; different processes sharing a broadly privileged machine identity are not an equivalent boundary.

This favors isolation over the smallest possible worker fleet. Per-customer runtime identities and partitions add lifecycle overhead but reduce credential exposure across customers. More cost-efficient execution designs require equivalent isolation evidence before adoption.

### 4. Durable metadata, minimized evidence and jobs

Use Azure Database for PostgreSQL for organizations, assignments, connections, encrypted distributed operator session/MSAL cache records, baseline/profile releases, assessments, findings, exceptions, emergency registries, plans, approvals, job state and audit references. Store connector credential references, never credential values, outside their protected credential service. Session/cache rows are keyed by user and tenant, encrypted with versioned envelope keys protected by Key Vault, expire and revoke independently, and are inaccessible to frontend queries that do not match both identities. Test horizontal failover, pooled-connection partition isolation, key rotation/re-encryption, key loss, backup restore, and revocation without session resurrection. Use Azure Blob Storage for encrypted, access-controlled configuration evidence and reports, and Key Vault for key material. Use least-privilege identities; the browser has no storage or queue access.

Important records are `CustomerConnection`, `ConnectorApplicationBinding`, `EncryptedOperatorSession`, `BaselinePackRelease`, `EffectiveProfileRelease`, `Assessment`, `EvidenceRecord`, `Finding`, `RiskException`, `EmergencyRegistryRevision`, `ChangePlanRevision`, `Approval`, `OperationExecution` and `AuditEvent`. Plan and evidence records retain source, adapter, evaluator and schema versions, hashes, timestamps and tenant ownership.

Use Service Bus with a transactional outbox to avoid committed jobs being lost between database and queue. Consumers are at-least-once: durable operation keys, resource-scoped leases and reconciliation handle duplicates. Enforce per-customer quotas, bounded retries, Retry-After-aware backoff, dead-letter diagnostics and compatible job schemas. Minimize queue payloads and TTL; never treat an old queue message as current authorization.

Evidence snapshots are append-only for their lifetime, integrity-checked and redacted. Preserve permitted original configuration data sufficient for reproducibility without collecting mailbox contents, documents, passwords or recovery material. Separate retention-governed evidence from protected audit history. Export workers cannot obtain customer write credentials.

Alternative: document-only storage makes relational approval/ownership constraints harder to enforce; inline report generation ties slow work to web requests.

### 4a. Workload profiles, network and cost gates

Define development/test, pre-production, and production profiles. Profiles specify Container Apps VNet integration, private DNS zones for private endpoints, private PostgreSQL/Blob/Key Vault/Service Bus paths, and controlled outbound routing for the Microsoft identity, Graph, ARM, and service-specific public APIs that cannot be privately hosted. Do not imply private endpoints exist for Microsoft APIs. Document DNS resolution, egress policy, required destinations, telemetry, and failure behavior per profile. Production uses Service Bus Premium; its cost and capacity, VNet/network profile, regional design, and workload sizing require explicit approval before provisioning.

The initial development/test defaults are proposals for fixture and synthetic environments only: per-customer active jobs <= 2; job execution timeout 30 minutes; at most 5 transient retries with capped backoff no longer than 15 minutes and honoring Retry-After; alert when queue age exceeds 10 minutes; reconcile uncertain writes within 10 minutes without retry before state is known; synthetic fixture retention 14 days; budget alerts at 75% and 90% of an approved test budget. Proposed recovery test targets are RTO 4 hours and RPO 15 minutes. These are not user-approved production SLOs, service guarantees, or customer data-retention defaults. Production thresholds and retention require explicit approval before activation.

### 5. Declarative baseline packs and review workflow

Use schema-validated declarative controls in reviewed release bundles, with stable IDs, citations, revision, review freshness, applicability, evidence contract, expected values, severity and explicit automation support. Publisher provenance and integrity metadata must be verifiable before activation. Baseline packs contain no executable remediation code; code lives only in reviewed adapter operations.

The source monitor records update notices, provenance and content diffs. Review/licensing checks produce a new immutable pack, never overwrite a release. Activation creates a new effective-profile release with a preview. Resolve platform -> MSP -> customer inheritance explicitly, record policy conflicts and choices, and pin assessments and plans to the result. Exception expiry changes actionability, not historical measured state.

Microsoft guidance and CISA SCuBA provide technical inputs; CIS content requires established reuse rights; SANS governance requirements can be manual evidence controls. Do not select a recommendation simply because it is stricter. Preserve framework mappings without claiming certification. Guidance age and unsupported-source warnings appear in the catalog.

Alternative: live webpage scraping into executable rules is current-looking but neither reviewed nor reproducible. Embedding unlicensed benchmark text is not an acceptable shortcut.

### 6. Per-service assessment contracts and initial coverage

An adapter manifest declares service/cloud support, source/API versions, runtime, read/write operations, permission and license prerequisites, pagination, throttling, evidence schema, scopes, effective-policy semantics, and supported recovery. Prefer supported stable APIs; use supported PowerShell modules where required. Distinguish unsupported data from API failures and partial collection. Reconfirm exact operations and permissions before implementing each adapter.

The following are starting control areas to turn into a source-backed, executable matrix, not a claim that every listed setting is already accessible:

| Module                        | Initial control areas and evidence                                                                                      | Important limitations                                                                            |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| Entra ID                      | Authentication settings, Conditional Access scope, privileged roles, guest/app consent and emergency account references | P1/P2 features and some operations require additional rights; scope evaluation can be incomplete |
| Exchange Online               | Organization security, forwarding restrictions, auditing, SMTP authentication and relevant policy scope                 | Use supported Exchange operations; per-mailbox differences matter                                |
| Teams                         | Federation, guests, meetings, application policies and assignments                                                      | Effective settings can depend on multiple policies                                               |
| SharePoint/OneDrive           | Organization/site sharing, external access, default link settings and exclusions                                        | Tenant defaults do not prove all sites comply                                                    |
| Power Platform/Power Automate | Environments, DLP, connectors, flow ownership and sharing                                                               | Inventory is not effective DLP evaluation; some evidence is manual                               |
| Intune                        | Enrollment, compliance, configuration/security profiles, assignments, exclusions, conflicts and deployment status       | Policy creation is not device compliance; coverage needs an expected inventory                   |
| Defender for Identity         | Sensor health/coverage and supported security configuration                                                             | On-premises or inaccessible settings are manual/unknown, not inferred passes                     |
| Defender for Office 365       | Anti-phishing, Safe Links, Safe Attachments, preset protection and recipient coverage                                   | Licensing, precedence and recipient exclusions affect results                                    |
| Defender for Endpoint         | Onboarding, security-setting/ASR/EDR configuration and deployment coverage                                              | Use expected device inventory; co-managed settings require source attribution                    |
| Defender for Cloud Apps       | Connected applications, governance policies and supported session controls                                              | Feature/API availability varies; do not infer coverage from a connected app alone                |
| Defender XDR                  | Supported cross-product integration, configuration and security access scope                                            | Correlate product findings rather than double-counting them                                      |
| Purview                       | Audit, DLP scope, information protection and relevant retention settings                                                | Preserve legal/retention context; potentially destructive changes remain guidance-only           |
| Azure                         | Resource configuration, Policy compliance, access, networking, logging and Defender for Cloud                           | Distinguish management/data planes and subscription/resource scopes                              |

Every module gets source-backed automated checks where supported, explicit manual controls where required, and fixture/contract tests for denied, partial, missing-license and unsupported conditions. A placeholder tile does not satisfy a module's assessment delivery. Initial delivery is commercial Microsoft cloud; other clouds remain visible unsupported scope until separately verified. A service being unlicensed is different from the adapter being unimplemented.
Every module gets source-backed automated checks only after operation support is verified, explicit manual controls where required, and fixture/contract tests for denied, partial, missing-license and unsupported conditions. A placeholder tile does not satisfy a module's assessment delivery. Initial delivery is commercial Microsoft cloud; other clouds remain visible unsupported scope until separately verified. A service being unlicensed is different from the adapter being unimplemented.

#### Initial source-backed control acceptance inventory

The following 13 stable IDs pin the first fixture-normalized control slice, one per declared service family. The expected values are proposed initial product defaults, not user-approved production policy or universal compliance claims. Source links are source families; implementation must capture the exact page/baseline revision, retrieval timestamp and content hash before a control is activated. Until the named operation, permission, license, and evidence semantics pass review and contract tests, support status is `manual/unverified`; this table does not assert that any API endpoint or module operation has been verified.

| ID       | Service and source reference                                                                                                                                       | Normalized fixture fields                                                                                            | Exact expected fixture state                                                                                                            | Initial support                                                                 |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| ENT-001  | Entra ID; [CISA SCuBA M365 baselines](https://github.com/cisagov/ScubaGear)                                                                                        | `privileged_role_ids`, `covered_role_ids`, `excluded_principal_ids`, `emergency_registry_ids`, `unknown_scope_count` | `covered_role_ids == privileged_role_ids`; `excluded_principal_ids` is a subset of `emergency_registry_ids`; `unknown_scope_count == 0` | Manual/unverified; prove read and effective-scope support before automation     |
| EXO-001  | Exchange Online; [Microsoft external forwarding policy guidance](https://learn.microsoft.com/defender-office-365/outbound-spam-policies-external-email-forwarding) | `external_auto_forwarding_allowed`, `scope_complete`                                                                 | `external_auto_forwarding_allowed == false`; `scope_complete == true`                                                                   | Manual/unverified; prove organization and recipient scope                       |
| TEAM-001 | Teams; [Microsoft Teams meeting policy guidance](https://learn.microsoft.com/microsoftteams/meeting-policies-in-teams)                                             | `anonymous_meeting_join_allowed`, `policy_assignment_complete`                                                       | `anonymous_meeting_join_allowed == false`; `policy_assignment_complete == true`                                                         | Manual/unverified; prove effective policy assignment                            |
| SPO-001  | SharePoint/OneDrive; [Microsoft default sharing link guidance](https://learn.microsoft.com/sharepoint/change-the-default-link-type)                                | `default_link_audience`, `default_link_permission`, `scope_complete`                                                 | `default_link_audience == organization`; `default_link_permission == view`; `scope_complete == true`                                    | Manual/unverified; tenant defaults do not prove site-level coverage             |
| PP-001   | Power Platform/Power Automate; [Microsoft DLP policy guidance](https://learn.microsoft.com/power-platform/admin/wp-data-loss-prevention)                           | `expected_environment_ids`, `covered_environment_ids`, `dlp_enabled`, `prohibited_connector_pair_tests`              | expected environments are a subset of covered; `dlp_enabled == true`; every prohibited pair test is blocked                             | Manual/unverified; prove environment and effective DLP support                  |
| INT-001  | Intune; [Microsoft device compliance guidance](https://learn.microsoft.com/mem/intune/protect/device-compliance-get-started)                                       | `expected_device_ids`, `compliant_device_ids`, `pending_device_ids`, `failed_device_ids`, `unassigned_device_ids`    | `compliant_device_ids == expected_device_ids`; pending, failed, and unassigned sets are empty                                           | Manual/unverified; require approved inventory and effective deployment evidence |
| MDI-001  | Defender for Identity; [Microsoft sensor health guidance](https://learn.microsoft.com/defender-for-identity/deploy/configure-sensor-settings)                      | `expected_domain_controller_ids`, `healthy_sensor_ids`, `unknown_sensor_ids`                                         | healthy sensors equal expected domain controllers; unknown sensor set is empty                                                          | Manual/unverified; inaccessible/on-premises evidence remains unknown            |
| MDO-001  | Defender for Office 365; [Microsoft Safe Links policy guidance](https://learn.microsoft.com/defender-office-365/safe-links-policies-configure)                     | `safe_links_enabled`, `allow_click_through`, `expected_recipient_ids`, `covered_recipient_ids`                       | `safe_links_enabled == true`; `allow_click_through == false`; covered recipients equal expected recipients                              | Manual/unverified; prove precedence, licenses and recipient scope               |
| MDE-001  | Defender for Endpoint; [Microsoft tamper protection guidance](https://learn.microsoft.com/defender-endpoint/enable-tamper-protection)                              | `expected_device_ids`, `tamper_protected_device_ids`, `unknown_device_ids`                                           | tamper-protected devices equal expected devices; unknown device set is empty                                                            | Manual/unverified; prove expected inventory and device coverage                 |
| MDCA-001 | Defender for Cloud Apps; [Microsoft policy guidance](https://learn.microsoft.com/defender-cloud-apps/policies)                                                     | `expected_app_ids`, `covered_app_ids`, `enabled_governance_policy_ids`                                               | covered apps equal expected apps; every in-scope app has at least one enabled governance policy                                         | Manual/unverified; app inventory alone is not policy coverage                   |
| XDR-001  | Defender XDR; [Microsoft Defender XDR overview](https://learn.microsoft.com/defender-xdr/microsoft-365-defender)                                                   | `expected_product_ids`, `integrated_product_ids`, `unknown_product_ids`                                              | integrated products equal expected licensed products; unknown product set is empty                                                      | Manual/unverified; verify documented configuration evidence before automation   |
| PUR-001  | Purview; [Microsoft audit enablement guidance](https://learn.microsoft.com/purview/audit-log-enable-disable)                                                       | `unified_audit_log_enabled`, `evidence_fresh`                                                                        | `unified_audit_log_enabled == true`; `evidence_fresh == true`                                                                           | Manual/unverified; confirm permission, license, and current evidence path       |
| AZR-001  | Azure; [Microsoft secure transfer requirement](https://learn.microsoft.com/azure/storage/common/storage-require-secure-transfer)                                   | `expected_storage_account_ids`, `secure_transfer_required_ids`, `unknown_account_ids`                                | secure-transfer-required accounts equal expected accounts; unknown account set is empty                                                 | Manual/unverified; prove ARM scope and complete resource inventory              |

Each fixture records `control_id`, `source_url`, `source_revision`, `retrieved_at_utc`, `content_hash`, `fixture_schema_version`, `tenant_id`, `resource_ids`, `collected_at_utc`, `normalized_fields`, `expected_state`, `observed_state`, `support_mode`, `permission/license/runtime prerequisites`, and `evidence_references`. Use test names `control_<ID>_<case>` and retain case distinctions for compliant, noncompliant, denied, partial, stale, unsupported, missing-license, and manual evidence. If any expected value conflicts with the pinned source or customer-approved profile, block activation and record the resolution rather than silently changing the fixture.
Use Service Bus Premium for production with a transactional outbox to avoid committed jobs being lost between database and queue. Consumers are at-least-once: durable operation keys, resource-scoped leases and reconciliation handle duplicates. Enforce per-customer quotas, bounded retries, Retry-After-aware backoff, dead-letter diagnostics and compatible job schemas. Minimize queue payloads and TTL; never treat an old queue message as current authorization. Batch evidence writes with bounded item/byte limits, resumable checkpoints, stable idempotency keys, and per-batch source/version manifests so partial collection cannot be mistaken for complete coverage.

Inventory-based coverage uses customer-approved expected scope and a named inventory source, not a guessed denominator. Canonical controls can map to multiple services/frameworks without duplicate risk scoring. Collection yields normalized typed evidence, and evaluators produce the six statuses in the specs with reasons. Drift comparisons separate configuration change from baseline/evaluator change.

### 7. Remediation as a guarded state machine

Use explicit states: draft -> pending approval -> approved -> queued -> preflight -> executing -> pending verification -> verified; also blocked, expired, failed, partially completed, cancelled and unknown. Store per-operation state because service APIs are not one atomic transaction.
Before adapter expansion or remediation UI, run a feasibility gate for every proposed operation: source revision, read permissions and complete scope, exact target setting, service-side conditional-write/ETag or equivalent race protection, post-write read and effective-state verification, propagation bounds, and recoverability/manual recovery. Verify in synthetic fixtures and documentation first. If any gate is unsupported or unproven, the operation stays guidance-only. Any later live contract test is a separate task requiring explicit approval for named disposable test tenants/resources and is never part of unattended CI.

Hash a canonical plan including tenant, targets, before/after values, relevant configuration fingerprint, source/profile/adapter revisions, emergency-registry revision, impact and expiry. Approvals reference the immutable revision/hash. Default to an independent approver; customer-required approvals are configurable. Resolve current authorization at admission and before each privileged call; no approval elevates service permissions.

Preflight re-reads configuration and effective scope, rechecks relationships, leases, stop flags, audit availability, emergency readiness and API compatibility. Exclude volatile irrelevant fields from fingerprints through reviewed normalization, not ad hoc drift suppression. Use conditional writes/ETags where supported. A local lease alone does not prevent external administrator races; an operation without adequate service-side concurrency semantics remains guidance-only.

Store durable intent before a write. Reconcile timeouts against remote state before retrying. Poll verification with bounded propagation windows using native settings/effective deployment, not just a successful API response. Never roll back automatically across services or claim cancellation reverted completed changes. Recovery is a new approved plan with its own drift and safety checks.

The initial allowlist is empty by default. Qualification tasks must enable a small set of reversible, explicitly tested operations; they must not expand the list merely to fill the UI. Candidate controls include SharePoint default link audience/permission settings, subject to current source guidance, API concurrency support, test-tenant validation and impact review. If a candidate cannot meet the contract, keep it guidance-only and qualify another supported control. Identity enforcement, protection disablement, privileged access removal and destructive retention actions stay guidance-only initially.

Alternative: a generic "fix all" action or arbitrary PowerShell executor cannot provide exact-plan approval, safe concurrency or reliable recovery.

### 8. Emergency access is a safety dependency

Keep a customer-approved, immutable-ID registry with intended recovery roles, control-specific exclusions and customer-run test records. Recommend at least two independently usable cloud emergency-access accounts; the application does not create, store credentials for, or sign into them. Exclusions must not weaken all authentication controls indiscriminately.

Lockout-sensitive plans check account existence, enabled state, supported role/scope evidence and current recovery-readiness review. Evaluate all relevant overlapping supported policies; unknown dependencies fail closed. Registry edits require approval and invalidate dependent plans. Ordinary remediation cannot disable/delete protected accounts or change their authentication/recovery properties.

Display exclusions as reviewed exceptions, retain measured security findings and inspect available sign-in/monitoring configuration. An alert rule existing is not proof its delivery was tested. Out-of-band customer recovery tests remain necessary because no policy analyzer guarantees all access paths.

### 9. Lighthouse as complementary access paths

Record GDAP expiry, Microsoft 365 Lighthouse eligibility, Azure delegated scopes, and direct service consent independently. Check the actual selected path before execution and do not silently fail over to broader credentials.

Use Microsoft 365 Lighthouse portal navigation for supported production workflows. Its `managedTenants` API was documented as preview during this exploration, and the inspected tenant-list endpoint supports delegated user access only. Keep preview evaluation disabled by default and separate from production findings and writes; verify release status before future promotion. No private endpoint scraping or unsupported portal embedding.

Azure Lighthouse can support authorized Azure management operations at delegated subscription/resource-group scope. Validate supported built-in role/actions; do not assume Owner, custom roles, directory rights, or DataActions support. GDAP does not automatically grant application permissions. Standalone and ineligible customers use direct authorization with visible coverage limitations.

Alternative: making Lighthouse mandatory excludes eligible direct-service customers and would couple production evidence to preview/API limitations.

### 10. Reporting and operator experience

Build an MSP overview limited to assigned customers and explicit customer workspaces: Connections, Coverage, Assessments, Findings, Change Plans, Reports, Exceptions and Audit. Keep customer name and tenant ID persistent in privileged views. Provide dense sortable/filterable tables, per-service states, accessible keyboard interactions, current/expected diffs, impact, evidence freshness and clear blocked-action reasons. Use a consistent accessible component library during implementation, not dashboard marketing panels.

Generate HTML/PDF and JSON/CSV from the same immutable report model. Use a bounded report worker and a tested browser-rendering tool for PDF. Authorize retrieval separately from generation; avoid long-lived public download URLs. Encode HTML, prevent CSV formula execution, and disable external network/file access during rendering of customer-derived content. Keep responsive text and layouts usable on desktop and mobile.

Report summaries expose numerators, denominators, coverage and statuses. Accepted risks and unknowns are not passes. Render the exact proposed configuration delta separately from evidence and actual execution results. An external portal visit cannot mark a finding fixed; reassessment must supply fresh proof.

### 11. Operational and release safeguards
Use Azure Monitor/Application Insights with redacted tenant/job correlation and restricted audit storage. Keep signed-in customer content and credentials out of general telemetry. App writers cannot mutate prior audit events. A write requires durable audit intent; audit loss after a write creates an explicit reconciliation incident.

Use Azure Monitor/Application Insights with redacted tenant/job correlation and restricted audit storage. Keep signed-in customer content and credentials out of general telemetry. App writers cannot mutate prior audit events. A write requires durable audit intent; audit loss after a write creates an explicit reconciliation incident.

Use a structured, setting-level audit event contract: event ID/type, server UTC timestamp, organization/customer tenant IDs, validated authorized user ID and display snapshot, requester, approver references, executing workload identity, M365/Azure service, resource ID, setting name/path, typed pre-write value, requested new value, separately observed verified value, outcome, error category and plan/operation/correlation IDs. Preserve null, absent, unknown and redacted as distinct values. Actor IDs come from authenticated/stored authorization context, never a request-provided display name. Scheduled and external changes are labeled accordingly; observed drift does not prove who changed a setting.

Persist append-only intent, result and verification events, not a mutable row claiming final success. Record each setting in a multi-setting operation and correlate it to the shared operation; unknown and partial outcomes remain explicit until later reconciliation events. Apply field-level secret redaction before persistence and again at rendering/export. Do not retain tokens, certificates' private material, passwords or recovery codes as old/new values. Protect history against normal application update/delete access, record integrity metadata and archive to retention-controlled protected storage; corrections append events instead of editing history.

Provide a customer-scoped Audit view with UTC range, user, service, setting, resource and outcome filters, a readable before/after diff, and JSON/CSV export using the same redaction and retrieval authorization as reports. Compliance reports link to these events. Operational telemetry is not the authoritative change ledger. Unit and integration tests must prove required fields, actor attribution, failed/unknown outcome handling, durable-intent enforcement, append-only permissions, redaction and cross-customer export denial.

Customer activation requires approved geography, evidence/audit retention, backup retention and deletion policy. Do not pick a customer geography from the MSP's billing location. Offboarding disables admission and credentials, stops future writes, removes platform-controlled grants where permitted, and reports external grants requiring customer action. Honor legal holds and disclose backup expiry.

Restore into execution-disabled mode, quarantine restored authorization and jobs, and reconcile with current customer grants and external operation state before enabling workers. Deployment rollback preserves evidence/audit and blocks incompatible job schemas; it never restores customer settings implicitly.

Use infrastructure as code, managed-identity deployment, dependency/security scans and environment separation. Development and CI use synthetic/redacted fixtures, not production credentials. Live integration tests require explicitly authorized disposable test tenants/resources and never execute automatically against MSP customer tenants.
Use infrastructure as code, managed-identity deployment, dependency/security scans and environment separation. CI builds immutable, provenance-recorded image digests and promotes the exact tested digest; it runs regression suites for all eight capability specifications plus schema and backward-compatible migration tests. Database migrations use expand/contract compatibility and are tested against prior supported app/worker versions. Deployment gates reject mutable image tags in release manifests and reject incompatible job/evidence schemas. Development and CI use synthetic/redacted fixtures, not production credentials. Live integration tests require explicitly authorized disposable test tenants/resources and never execute automatically against MSP customer tenants.

Release milestones gate progression: (G0) foundation, identity/session partitions, audit, schema and fixture contracts; (G1) source-revision-pinned 13-control inventory and matrix acceptance; (G2) read-only evidence and per-service coverage; (G3) read/conditional-write/verify/recovery feasibility with no broad remediation or remediation UI before pass; (G4) one-at-a-time allowlist qualification with exact approval and disposable-scope evidence; (G5) security/regression/migration/recovery gates and customer pilot approval. Each milestone publishes a bounded evidence bundle with source revisions, test identifiers/results, image digest, migration compatibility, unresolved support gaps, approvals, and stop conditions. A failed gate blocks promotion; no service or break-glass/audit requirement is removed to meet a milestone.

## Risks / Trade-offs

- [Central hosting concentrates sensitive configuration and delegation] -> Per-tenant/purpose identities, row-level enforcement, minimized evidence, restricted controller access and isolation tests.
- [Next.js and .NET increase operating overhead] -> Keep the BFF thin, use one API contract and independent health/version checks.
- [Microsoft APIs and benchmark guidance change] -> Pin versions, monitor changes, review releases and run adapter contract tests before promotion.
- [Some services need Windows, extra licenses or unavailable evidence] -> Compatible isolated workers and explicit manual/unknown coverage; no fabricated pass results.
- [Broad coverage could become superficial] -> A control/permission matrix and tested real evidence paths per service are release gates; staged delivery does not redefine scope.
- [Customer changes race with automation] -> Relevant-state fingerprints, remote conditional writes and guidance-only fallback when concurrency safety is insufficient.
- [Emergency exclusions can create an exposure] -> Control-specific exceptions, monitoring visibility, periodic customer recovery tests and no blanket exemption.
- [Baseline content rights vary] -> Rights review before bundling, source citations and no automatic redistribution of restricted text.
- [Per-customer worker definitions and identities add cost] -> Provision bounded jobs and Windows capacity only when needed; validate costs before deployment, not by weakening credential isolation.

## Migration Plan

This is a greenfield application addition; no existing lab utility migration is required.

1. Establish the application skeleton, identity/isolation contracts, database and job schemas, source-backed control matrix and fixture harness in the new application area.
2. Deliver read-only assessments, emergency registry and reporting incrementally across every declared service family, showing gaps throughout. Do not advertise complete scope until service acceptance gates pass.
3. Validate two synthetic/test customer tenants plus standalone mode, including negative authorization, cache, queue and evidence tests. Complete all eight capability scenario suites.
4. Qualify a small remediation allowlist in disposable test scope, run drift/duplicate/unknown-outcome/recovery cases, and require operator approval in a pilot.
5. Before any real Azure deployment, confirm region, scale, budget, retention and customer authorizations; generate and validate infrastructure in a separately authorized implementation/deployment workflow. No resources are provisioned by this plan.
6. Release to pilot customers read-only first; enable only approved tested remediation capabilities after evidence review. Roll back application versions by disabling job admission and preserving audit/evidence; customer rollback uses separately approved recovery plans.

## Open Questions

- Production Azure geography, instance sizes, network profile, Service Bus Premium budget/capacity, concurrency, availability/recovery SLOs, evidence/audit retention and deletion durations remain explicit approvals before deployment or customer activation. Development/test values above are proposals only, not production commitments.
- Product name and visual identity can be selected before UI implementation without changing navigation, accessibility or authorization behavior.

## References

Source guidance was consulted during exploration on 2026-11-01; reverify endpoint versions, feature availability, permissions, licensing and eligibility before implementing adapters.

- Microsoft identity flows: https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow
- Microsoft Secure Score and its coverage limitations: https://learn.microsoft.com/defender-xdr/microsoft-secure-score
- Defender for Cloud policy model: https://learn.microsoft.com/azure/defender-for-cloud/security-policy-concept
- Microsoft 365 Lighthouse API: https://learn.microsoft.com/graph/managedtenants-concept-overview
- Inspected tenant-list permission and beta status: https://learn.microsoft.com/graph/api/managedtenants-managedtenant-list-tenants?view=graph-rest-beta
- Lighthouse eligibility: https://learn.microsoft.com/microsoft-365/lighthouse/m365-lighthouse-requirements?view=o365-worldwide
- Azure Lighthouse role boundaries: https://learn.microsoft.com/azure/lighthouse/concepts/tenants-users-roles
- CISA ScubaGear: https://github.com/cisagov/ScubaGear
- CIS Benchmarks: https://www.cisecurity.org/cis-benchmarks
- SANS policy guidance: https://www.sans.org/information-security-policy/