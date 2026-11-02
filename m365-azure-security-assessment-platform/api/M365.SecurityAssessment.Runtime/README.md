# Security Assessment Runtime

This library contains pure admission policies, immutable runtime contracts, operation state transitions, and storage/transport interfaces. It has no database provider, Microsoft Graph/ARM client, Service Bus SDK, credential material, or production in-memory store. No source constructor performs network access.

## Composition Boundary

The API/worker composition root should reference this project and register concrete adapters for the interfaces it owns. The shared solution file and application projects are intentionally not changed by this slice.

Production registrations must use durable implementations. In particular:

- `ITransactionalOutboxStore.PersistWithJobAsync` must persist the job and outbox row in the same database transaction. Dispatch is at-least-once; consumers deduplicate by tenant, plan revision/hash, and operation ID.
- `IServiceBusTransport` is optional at composition time. With no transport, pending rows stay pending and are reported as `PendingTransportUnavailable`; an outbox row is marked dispatched only after broker acknowledgement.
- `IStoredJobOwnershipReader` loads authoritative ownership. Call `ExecutionBindingPolicy.Evaluate` before resolving any credential reference. Queue fields never establish ownership.
- `IStoredCredentialReferenceResolver` returns only a tenant/purpose/revision-bound reference. It is not a token or secret store. Assessment and remediation references must remain separate.
- `IOperationIdempotencyStore` must enforce a durable unique operation key. `IResourceLeaseStore` must provide customer/resource/setting-scoped leases across worker instances.
- `IOperationQualificationStore` must return only reviewed, current operation feasibility evidence from trusted storage; caller-supplied evidence must never qualify a write.
- `ISettingStateReader` must return fresh setting, scope, permission, connection, emergency-registry, stop-flag and ETag evidence. Unknown or stale evidence blocks.
- `IDurableAuditLedger.TryAppendIntentAsync` must confirm append-only durable intent before any future conditional writer is called. Audit persistence failure blocks the write. Result and verification events append; they never overwrite intent.
- `ITenantExecutionQuotaStore` must enforce limits across replicas, not within a process. `ICustomerLifecycleStore` must persist stop, disable, cancellation, and observed completion status.

The required future per-setting coordinator order is: validate ownership and current authorization; deduplicate; acquire the durable resource lease; validate exact-plan approval; fetch fresh preflight and stop state; validate emergency readiness when lockout-sensitive; append durable audit intent; recheck cancellation/stop immediately before a supported conditional write; append the result; read back and verify within a bounded propagation window. A lost response becomes `Unknown`; reconcile with a remote read before considering retry. A recovery is a new plan and approval, never automatic snapshot restoration.

`IConditionalSettingWriter` is only a contract. There is no implementation to register. `MutationAllowlist.EnabledOperationIds` is empty, and remediation admission remains blocked until operation-specific feasibility evidence and reviewed qualification exist. No call in this library can mutate a customer tenant.

Partner operations must name the selected path (`DirectConsent`, `Gdap`, or `AzureLighthouse`) on each request. Failed relationships do not select a fallback. Microsoft 365 Lighthouse preview evaluation remains disabled by default and cannot feed production findings or writes. Portal navigation accepts only named routes.

## Task Candidate Boundary

This project supplies the following pure policy/contracts for coordinator review; it does not mark shared OpenSpec tasks complete:

- `6.1-6.7`: outbox dispatch contract, job/connection/tenant/purpose binding, purpose-scoped credential references, retry/quota abstractions, compatible-runtime guard, operation feasibility gate, and disposable-scope live-test authorization. Persistent outbox/quota implementations, worker credential lookup ordering, verified operation evidence, and live-test authorization storage remain integration gates.
- `12.1-12.4`: immutable-ID customer-confirmed registry contract, fail-closed account/readiness/scope preflight, protected-property checks, registry-revision invalidation, and monitoring/readiness view. Customer registry approval persistence and live directory/policy/telemetry evidence remain integration gates.
- `13.1-13.4`: independent GDAP/direct/Azure delegation state, documented-revision eligibility input, explicit per-operation path selection, target-scope/role/expiry checks, supported portal routes, and default-disabled preview policy. Current Microsoft eligibility source review and connector evidence remain integration gates.
- `15.1-15.7` and `15.9`: canonical plan hashing, exact independent/customer approval, fresh preflight, durable lease/idempotency/audit interfaces, unknown/partial/reconciliation/stop state policy, conflict-safe recovery-plan creation, and operation prerequisite documentation. Persistent adapters, tested remote conditional writes, per-setting audit event storage, and recovery proof remain gates. The allowlist intentionally stays empty.
- `17.2`, `17.4`, `17.5`, and `17.7`: explicit production-approval gate, status-bearing offboarding contract, restore quarantine with current authorization/job/approval reconciliation, and measurable development/test defaults that cannot qualify for production.

## Verification

Run the isolated runtime test suite with the pinned .NET 10 SDK:

```powershell
& 'C:\Users\DEV\AppData\Local\Microsoft\dotnet\dotnet.exe' test 'm365-azure-security-assessment-platform\tests\backend\M365.SecurityAssessment.Runtime.Tests\M365.SecurityAssessment.Runtime.Tests.csproj'
```

The suite uses test-only fakes for outbox/transport behavior. Those fakes are not production registrations. It does not connect to Microsoft services, Azure, Service Bus, or customer tenants.