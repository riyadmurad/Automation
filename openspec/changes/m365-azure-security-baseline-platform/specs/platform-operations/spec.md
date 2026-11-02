# Spec Delta

## Purpose

Operate the platform securely from the MSP's Azure environment or a standalone customer deployment with isolated workers, resilient jobs, protected evidence, and controlled data lifecycle.

## ADDED Requirements

### Requirement: Central and standalone deployment modes
The platform SHALL support central MSP hosting of its interface, API, storage, queues, and workers in the MSP's Azure environment and the same components in single-customer standalone mode. Customer-hosted workers SHALL NOT be required for the initial release.

#### Scenario: Operator deploys standalone mode
- **WHEN** the deployment is initialized for one customer
- **THEN** the platform enforces the single-customer restriction without removing tenant isolation or safety checks

### Requirement: Isolated worker execution
Assessment, remediation, and platform administration SHALL have distinct execution permissions. Workers SHALL acquire only the selected customer's authorized connection, validate job ownership and operation type, and expose no public execution endpoint. Platform-specific adapters SHALL run only in compatible isolated runtimes.

#### Scenario: A Linux runtime receives a Windows-only adapter job
- **WHEN** its prerequisites do not match the adapter manifest
- **THEN** the job is routed to a compatible authorized worker or reported blocked, never falsely reported complete

### Requirement: Secure storage and network access
The platform SHALL encrypt metadata, evidence, and credentials in transit and at rest; restrict service access using least privilege; and keep internal storage, queues, and worker endpoints non-public. Credential access and rotation SHALL be auditable without logging secret values.

#### Scenario: A frontend process attempts direct secret retrieval
- **WHEN** it requests a remediation credential
- **THEN** access is denied and the event is audited without exposing the credential

### Requirement: Approved workload network profiles
Workload profiles SHALL define VNet integration, private DNS for private endpoints, and controlled outbound routing to required Microsoft APIs. Production deployment SHALL require an explicitly approved profile and cost estimate before provisioning.

#### Scenario: Production profile is missing network or cost approval
- **WHEN** deployment is requested without an approved profile and estimate
- **THEN** provisioning is blocked and no production resources are created

### Requirement: Production queue tier and cost approval
Production workloads SHALL use Service Bus Premium. Deployment SHALL require explicit approval of its cost and capacity assumptions; development/test tiers SHALL NOT be represented as production-ready.

#### Scenario: Premium queue cost has not been approved
- **WHEN** a production deployment is prepared without Service Bus Premium cost approval
- **THEN** the deployment gate blocks and reports the missing approval

### Requirement: Conditional Windows worker lifecycle
When an enabled adapter manifest requires Windows, execution SHALL use an isolated Windows VM job host with documented provisioning, patching, isolation, credential access, and cleanup. Windows capacity SHALL NOT be hosted as an assumed Container Apps workload and SHALL NOT be provisioned when no enabled manifest requires it.

#### Scenario: Adapter manifest requires Windows runtime
- **WHEN** an approved job targets an enabled Windows-only adapter
- **THEN** it routes to a separately isolated Windows VM job host or remains blocked until that host is ready

### Requirement: Explicit operational policy boundaries
The platform SHALL distinguish proposed development/test defaults from production SLOs and operational limits. Production activation SHALL require explicit approval of measurable thresholds for availability, queue age, job duration, concurrency, retries, recovery, retention, and budget.

#### Scenario: Production thresholds are not approved
- **WHEN** an operator attempts customer activation without approved production boundaries
- **THEN** activation is blocked while proposed development/test defaults remain clearly labeled as non-production

### Requirement: Tenant-fair resilient scheduling
The platform SHALL enforce per-customer concurrency limits, bounded retries, throttling-aware backoff, cancellation, and dead-letter handling. A failing customer or adapter SHALL NOT monopolize shared execution capacity, and retries SHALL honor remediation idempotency and uncertainty rules.

#### Scenario: One customer repeatedly hits service throttling
- **WHEN** its jobs enter backoff or exhaust their retry budget
- **THEN** other customers continue receiving execution capacity and the affected jobs expose retry or failure state

### Requirement: Protected audit and operational visibility
The platform SHALL retain append-only, access-controlled audit history with tenant and job correlation, alert on security-relevant failures, and prevent secrets or unneeded personal data entering telemetry. Remediation SHALL fail closed if its required durable audit record cannot be written.

#### Scenario: Audit persistence fails before a write
- **WHEN** a remediation worker cannot record its intent durably
- **THEN** it blocks the customer write and raises an operational failure

### Requirement: Explicit residency and retention policy
Before activating a customer, the platform SHALL record approved hosting geography, evidence and audit retention, deletion behavior, and backup retention. It SHALL not silently transfer evidence to another geography or retain customer content indefinitely by default.

#### Scenario: Customer onboarding lacks a retention policy
- **WHEN** activation is requested
- **THEN** the platform requires an approved policy before collecting customer configuration evidence

### Requirement: Safe customer offboarding
Offboarding SHALL disable new jobs and credential use, stop pending writes, revoke platform-controlled connection access, and delete or retain evidence according to policy and legal holds. It SHALL report remaining external grants and backup expiry instead of claiming immediate erasure everywhere.

#### Scenario: Customer is offboarded during queued remediation
- **WHEN** the job is received after offboarding begins
- **THEN** the worker blocks execution and the offboarding report identifies retained records and any customer action needed to remove external grants

### Requirement: Tested recovery without authorization resurrection
The platform SHALL support tested backup restoration and compatible deployment rollback. Restored jobs, credentials, approvals, and connections SHALL be quarantined pending reconciliation so a restore cannot reactivate revoked access or replay old changes.

#### Scenario: A backup predates consent revocation
- **WHEN** it is restored into a recovery environment
- **THEN** customer execution stays disabled until current authorization and job state have been revalidated

### Requirement: Version-compatible execution
Deployment SHALL preserve compatible API, job, adapter, and baseline contracts or block incompatible execution. A component rollback SHALL NOT automatically reverse customer settings or discard audit history.

#### Scenario: Old worker receives a newer operation contract
- **WHEN** the worker does not support the job's declared version
- **THEN** it rejects or defers the job safely without invoking the customer API