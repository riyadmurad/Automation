# Assessment Library

This library owns the read-only assessment contract, baseline-candidate validation, and synthetic adapter harness. It is intentionally not wired into the existing API or solution in this slice. The coordinator can add a project reference and register it with:

```csharp
builder.Services.AddAssessmentServices(builder.Configuration);
```

Register an `IAssessmentTokenProvider` separately. It must request tokens for the stored customer tenant and declared resource scope; the assessment library never persists, returns, or logs access tokens. Configure `Assessment:BaselinePackPath` for `IBaselinePackProvider.LoadConfiguredCandidate()`. Loading publishes an immutable candidate in the in-memory catalog; it does not activate a baseline or effective profile. Production wiring still needs a durable tenant-partitioned catalog and trusted approval/authorization context.

## Adapter Contract

An `AdapterManifest` is versioned and names a fixed Microsoft API origin, service, cloud, API version, runtime, support mode, permission/license/runtime prerequisites, and only explicitly allowlisted operations. Production-enabled operations must have a successful documentation URL and SHA-256 retrieval hash that qualify the actual API operation. The source pages in the qualified pack are product-setting guidance, not API operation documentation; therefore none of those pages qualify a collector endpoint.

Only `GET` and explicitly declared read-only `POST` query operations are representable. `POST` is allowed because some supported APIs expose read/query operations through POST; a method name alone does not establish read safety. The manifest must classify the operation as read-only, declare a JSON query body policy, and carry operation-specific documentation qualification. Write-classified operations, other HTTP methods, unlisted operation IDs, and body/method mismatches are rejected. No generic request URL or arbitrary verb is accepted.

Pagination uses the manifest's continuation field and an authorized origin/path prefix. Links with a different scheme/host/port, user information, fragments, encoded path traversal, or a path outside the operation prefix fail closed. The typed HTTP registration disables redirects. Throttling honors `Retry-After` within configured bounds. A denied/failed later page, invalid response, or page limit returns an incomplete checkpoint; a partial page is never marked complete. Response bodies and tokens are not included in error messages.

Expected inventories are separate, typed, tenant-bound evidence. Every required inventory must be approved, sourced, nonempty, and fully covered. A zero denominator cannot pass. Normalized evidence pins contract version, organization, customer tenant, baseline pack ID/version/hash, control, service, source URL/revision/hash, adapter version, collection time, evidence references, current values, inventory provenance, and per-inventory coverage. Results retain the evaluator version, source/support review state, current values, expected rules, deltas, reason, evidence references, and expected/covered counts.

Conditional Access scope analysis resolves only concrete principal/application IDs with complete assignments and supported conditions. It reports exclusions and overlaps; report-only policies are not treated as enforcing coverage. An unknown policy, unsupported condition, or incomplete inventory produces `Unknown`, not a pass. This is a scope analyzer, not a complete Conditional Access authorization simulator.

## Supported-Service Matrix

All declared families are present in `InitialAdapterCatalog`, but no service has an enabled live operation. This is deliberate: currently pinned source pages describe product settings, not the API path, operation semantics, permissions, licensing, or effective-scope guarantees needed to enable a collector. The manifest records a visible manual/unverified gap for each family; no PowerShell command or endpoint is represented as supported.

| Control | Service family | Current state | Unresolved evidence/operation boundary |
| --- | --- | --- | --- |
| ENT-001 | Entra ID | Manual/unverified | Privileged-role source release, supported read path, effective Conditional Access scope, and emergency registry binding |
| EXO-001 | Exchange Online | Manual/unverified | Organization plus recipient forwarding scope and supported read operation |
| TEAM-001 | Teams | Manual/unverified | Effective policy assignment and supported read operation |
| SPO-001 | SharePoint/OneDrive | Manual/unverified | Tenant/site scope; tenant defaults do not prove site-level settings |
| PP-001 | Power Platform/Power Automate | Manual/unverified | Environment inventory, DLP evaluation, and prohibited connector-pair outcomes |
| INT-001 | Intune | Manual/unverified | Approved device denominator and effective assignment/deployment state |
| MDI-001 | Defender for Identity | Manual/unverified | Domain-controller inventory, sensor state, and isolated Windows/on-premises runtime |
| MDO-001 | Defender for Office 365 | Manual/unverified | Safe Links precedence, license, recipient scope, and click-through state |
| MDE-001 | Defender for Endpoint | Manual/unverified | Tamper-protection read path, management authority, co-management, and device coverage |
| MDCA-001 | Defender for Cloud Apps | Manual/unverified | The pinned replacement page is Conditional Access app control, not governance policy coverage |
| XDR-001 | Defender XDR | Manual/unverified | Licensed-product denominator and configuration evidence separate from incidents |
| PUR-001 | Purview | Manual/unverified | Audit/DLP permissions, license, and evidence freshness |
| AZR-001 | Azure | Manual/unverified | ARM operation, subscription/resource-group boundary, and complete storage-account inventory |

## Fixture Authoring

`M365.SecurityAssessment.Assessment.Tests` generates explicitly synthetic evidence fixtures from the qualified pack rules and runs a fake `HttpMessageHandler`; no test has a network-capable handler or live-tenant identity. Each stable control ID gets named `matching-candidate`, `mismatching-candidate`, `denied`, `partial`, `stale`, `unsupported`, `unsupported-cloud`, `missing-license`, `missing-permission`, `throttled`, `error`, `manual`, `missing-field`, and `missing-inventory` cases. Since the checked-in controls remain manual/unverified, candidate matches and mismatches produce `ManualReview` with their deltas; only a separate synthetic test-only qualified definition exercises Compliant/NonCompliant mapping.

Fixture inventories must use synthetic IDs, bind to the same organization/tenant as evidence, and never be reused as production scope. A matching fixture is not source qualification, tenant evidence, or compliance certification.