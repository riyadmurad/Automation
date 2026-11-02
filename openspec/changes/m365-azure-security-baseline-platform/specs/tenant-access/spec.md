# Spec Delta

## Purpose

Authorize operators and service connections for explicitly assigned customer tenants while enforcing the same isolation boundaries in MSP and standalone deployments.

## ADDED Requirements

### Requirement: Operator authentication
The system SHALL authenticate operators through Microsoft Entra ID using OIDC and OAuth 2.0 authorization code flow with PKCE and validate issuer, audience, tenant, expiry, and session integrity. Sign-in alone SHALL NOT grant customer access.

#### Scenario: Signed-in operator has no assignment
- **WHEN** an authenticated operator requests a customer without an active platform assignment
- **THEN** the system denies access without returning that customer's data

#### Scenario: Invalid authentication context
- **WHEN** a request presents an expired token or an untrusted issuer or audience
- **THEN** the system rejects the request before accessing customer resources

### Requirement: Customer-scoped role enforcement
The system SHALL enforce Viewer, Assessor, Approver, Operator, and organization-administration permissions per management organization and customer. Customer approval requirements and separation of proposer and approver SHALL be configurable per customer and enforced by the API.

#### Scenario: Viewer attempts a change
- **WHEN** a Viewer submits an assessment or remediation request
- **THEN** the system denies the operation regardless of visible interface controls

#### Scenario: Independent approval is required
- **WHEN** a plan author attempts to approve their own plan for a customer requiring independent approval
- **THEN** approval is denied and the attempt is audited

### Requirement: End-to-end tenant isolation
The system SHALL bind data access, job execution, evidence, exports, token caches, and cached responses to an authorized management-organization and customer-tenant pair. A caller-supplied tenant identifier SHALL NOT establish authorization.

#### Scenario: Cross-customer object identifier is supplied
- **WHEN** an operator authorized only for customer A requests a finding, report, or approval belonging to customer B
- **THEN** the system rejects the request and returns no customer B content

#### Scenario: Worker envelope does not match stored ownership
- **WHEN** a queued job's organization or tenant differs from its stored job and connection ownership
- **THEN** the worker rejects execution before acquiring service credentials

#### Scenario: Cached content belongs to another customer
- **WHEN** two operators access equivalent routes for different customers
- **THEN** neither receives the other's cached content or service tokens

### Requirement: Customer connection authorization
The system SHALL record customer-authorized service scopes, permission grants, connection health, and credential references separately from operator login. It SHALL request assessment and remediation access separately and disclose missing permissions before enabling an operation.

#### Scenario: Assessment consent is present without write consent
- **WHEN** an authorized operator requests remediation through an assessment-only connection
- **THEN** the system blocks the write and identifies the additional customer authorization required

### Requirement: Restricted credential access
The system SHALL keep service credentials and tokens out of browser storage, reports, and logs. Assessment execution SHALL NOT access remediation credentials, and credential rotation SHALL preserve ownership boundaries and expose connection failures safely.

#### Scenario: Assessment worker requests write credentials
- **WHEN** an assessment worker attempts to obtain a remediation credential
- **THEN** credential access is denied and recorded without exposing secret values

### Requirement: Customer- and purpose-specific connector applications
Each customer SHALL use distinct single-tenant connector app registrations for assessment and remediation purposes. Shared multitenant connector signing credentials SHALL NOT be delivered to workers; a worker SHALL obtain only the credential bound to its stored customer and purpose.

#### Scenario: Worker job tenant or purpose does not match the credential binding
- **WHEN** a queued job requests credentials whose customer tenant or purpose differs from the stored job, connection, or preconfigured worker definition
- **THEN** the worker rejects the job before credential retrieval and records a redacted binding failure

#### Scenario: Customer connector credentials are rotated
- **WHEN** an authorized administrator rotates one customer's assessment or remediation credential
- **THEN** only that customer's matching purpose binding changes and other customers and purposes remain inaccessible

### Requirement: Distributed encrypted operator session persistence
Operator sessions and MSAL cache state SHALL persist across frontend instances in encrypted PostgreSQL records partitioned by user and tenant. Key version, expiry, revocation, rotation, and recovery SHALL be enforced without exposing cache material to the browser or unrelated tenants.

#### Scenario: Frontend instance fails during an active session
- **WHEN** an authenticated operator is routed to another frontend instance
- **THEN** the instance restores only the authorized user's tenant-partitioned session and MSAL state without requiring an in-memory cache from the failed instance

#### Scenario: Session encryption key is unavailable or rotated
- **WHEN** a cache record cannot be decrypted or its key version is retired
- **THEN** the system fails closed for downstream token use and records a redacted recovery event without returning another user's cache

### Requirement: Standalone customer restriction
The system SHALL support a standalone deployment bound to exactly one configured customer tenant without requiring CSP membership, GDAP, or Lighthouse. It SHALL reject onboarding or execution for any additional tenant.

#### Scenario: Second tenant is introduced in standalone mode
- **WHEN** an administrator submits a connection or job for a different customer tenant
- **THEN** the system rejects it even if valid external credentials are supplied

### Requirement: Revocation enforcement
The system SHALL prevent new operations after an operator assignment or customer connection is revoked. Queued and running operations SHALL recheck authorization before each new privileged action and stop when access is revoked.

#### Scenario: Authorization is revoked after enqueue
- **WHEN** a worker receives a previously authorized job whose customer connection is now disabled
- **THEN** it marks the job blocked or cancelled without calling the customer service