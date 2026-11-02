# Proposal

## Why

MSPs and individual organizations need a consistent, evidence-backed way to assess Microsoft 365 and Azure configurations against maintained security guidance, understand gaps, and safely apply approved improvements. Existing portals and benchmarks provide valuable but fragmented coverage; changes without tenant isolation, fresh configuration checks, and emergency-access safeguards can cause outages or administrator lockout.

## What Changes

- Introduce a modular security-baseline application with centrally hosted MSP operation in the MSP's Azure environment and a standalone deployment mode restricted to one customer tenant.
- Authenticate operators through Microsoft Entra ID using OIDC and OAuth 2.0, with tenant-scoped application roles and separate assessment and remediation access.
- Use customer-specific single-tenant connector app registrations per customer and purpose; do not deliver shared multitenant connector signing credentials to workers. Persist operator session/MSAL state across frontend instances using encrypted, user-and-tenant-partitioned PostgreSQL records.
- Maintain reviewed, versioned baseline packs sourced from Microsoft, CISA SCuBA, appropriately licensed CIS guidance, and applicable SANS policy guidance. Record provenance, applicability, conflicts, and coverage rather than claiming universal compliance.
- Assess all 13 declared service families: Entra ID; Exchange Online; Teams; SharePoint and OneDrive; Power Platform including Power Automate; Intune; Defender for Identity, Office 365, Endpoint, and Cloud Apps; Defender XDR; Purview; and Azure. Pin an initial source-backed control acceptance inventory with normalized fixtures, exact expected states, stable IDs, and honest automated/manual support; unsupported settings remain visible gaps.
- Query actual configurations and effective assignments, retain timestamped evidence, and generate detailed HTML/PDF and JSON/CSV reports comparing current settings with expected values and exact proposed changes.
- Provide operator-approved remediation for an explicit, tested allowlist only. Re-query before changes, bind approval to the plan, verify afterward, and expose partial failures and recovery limitations. High-risk or unsupported changes remain guidance-only.
- Protect customer-confirmed break-glass accounts using immutable IDs, control-specific exclusions, effective-scope checks, report-only/pilot stages where supported, and fail-closed lockout checks. Continue monitoring and assessing emergency access.
- Integrate Azure Lighthouse for supported delegated resource management and Microsoft 365 Lighthouse/GDAP for supported MSP workflows. Do not depend on preview Lighthouse APIs for production assessment or unattended remediation; retain direct service connectors and a standalone path.
- Provide customer-specific audit history, expiring exceptions, retention, offboarding, cancellation, and tenant-scoped operational controls.
- Qualify read, conditional-write, verification, and recovery feasibility before broad remediation or operator UI work. Keep live tests consent-gated and limited to explicitly authorized disposable tenants/resources.
- Define development/test operational defaults separately from production policy. Require explicit approval of production SLOs, network/workload profiles, Service Bus Premium cost, geography, retention, capacity, and budget before deployment or activation.
- Audit every attempted setting change with UTC timestamp, authorized user, M365/Azure service, resource and setting name, previous and requested new values, outcome and verification; provide tenant-scoped search/export and redact secrets.

This proposal authorizes planning only. Implementation is a separate apply workflow. Full service scope is retained while adapter delivery is incremental; a partial adapter is never represented as complete service coverage.

## Capabilities

### New Capabilities

- `tenant-access`: MSP organizations, customer connections, standalone restrictions, OIDC/OAuth, scoped roles, consent and credential lifecycle, and cross-tenant authorization boundaries.
- `baseline-catalog`: Versioned source-backed controls, reviewed updates, licensing, applicability, profile inheritance, and explicit conflict resolution.
- `service-assessment`: Modular service adapters, current-state collection, effective-scope evaluation, coverage disclosure, and scheduled assessments with explicit unknown outcomes.
- `evidence-reporting`: Tenant-scoped evidence, technical conformance reports, exact proposed changes, exceptions, exports, and assessment history.
- `safe-remediation`: Allowlisted changes, exact-plan approval, preconditions, idempotency, concurrency protection, verification, cancellation, and recovery.
- `emergency-access`: Customer-confirmed emergency account registry, targeted exclusions, lockout preflight checks, protected changes, and ongoing monitoring.
- `partner-integrations`: Independent Lighthouse/GDAP/direct-connector relationships, eligibility checks, production API boundaries, and revocation behavior.
- `platform-operations`: Central Azure and standalone operation, worker isolation, secure storage, per-tenant scheduling, audit, retention, recovery, and offboarding.

### Modified Capabilities

None. The project currently has no durable OpenSpec capabilities or security-baseline application.

## Impact

- New application components will be introduced in a dedicated project area during implementation; existing lab and Obsidian PowerShell utilities remain unchanged and are not production security defaults.
- External dependencies include Microsoft identity libraries, supported Microsoft Graph endpoints, service-specific APIs or supported PowerShell modules, Azure management APIs, and optional source adapters such as ScubaGear. Each operation requires permission, license, cloud and API-version verification.
- Proposed Azure hosting includes application and worker runtimes, a queue, relational metadata storage, protected evidence storage, a secrets service, and monitoring. Exact sizing, region, retention periods, and budgets require deployment decisions; no resources or customer authorizations are created by this change's planning stage.
- The first implementation must establish a traceable control/permission matrix and tested remediation allowlist across the declared service families, not promise that every product setting is automatable.
- Customer configuration data is sensitive. Isolation, evidence minimization, credential protection, data residency and offboarding are release requirements, not later enhancements.