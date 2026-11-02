# Spec Delta

## Purpose

Apply a limited set of tested, customer-scoped security changes only after exact-plan approval, fresh preflight checks, protected execution, and evidence-backed verification.

## ADDED Requirements

### Requirement: Explicit remediation allowlist
The system SHALL enable automated writes only for versioned adapter operations with verified permissions, tested scope and preconditions, impact description, verification, and recovery guidance. High-risk, unsupported, or unqualified controls SHALL remain guidance-only; baseline content SHALL NOT supply arbitrary executable remediation.

#### Scenario: An unqualified control has a recommended fix
- **WHEN** an operator requests automated application
- **THEN** the system refuses execution and provides the supported manual guidance

### Requirement: Operation feasibility gate precedes remediation expansion
An operation SHALL remain guidance-only until read evidence, service-side conditional-write or equivalent race protection, post-write verification, and recovery behavior are documented and fixture-tested. Failure of any gate SHALL prevent allowlist activation.

#### Scenario: Conditional-write support is unavailable
- **WHEN** feasibility review cannot establish safe concurrency for an operation
- **THEN** the operation remains guidance-only regardless of approval or UI availability

### Requirement: Consent-gated live contract tests
Live service contract tests SHALL require explicit authorization for disposable test tenants and resources, record the authorized scope, and SHALL NOT target customer tenants by default or run automatically in CI.

#### Scenario: A live test has no current scope authorization
- **WHEN** a live contract test is requested without explicit authorization for its disposable tenant and resources
- **THEN** the test is blocked before connecting to the service

### Requirement: Immutable customer-scoped change plans
A plan SHALL bind the customer tenant, targets, operations, before and after values, baseline and profile versions, adapter versions, evidence snapshot, safety exclusions, impact, expiry, and recovery limitations into an immutable revision. Cross-customer bulk remediation SHALL NOT be available in the initial release.

#### Scenario: A target or expected value changes
- **WHEN** a plan is edited after approval
- **THEN** a new revision is created and all prior approvals are invalid for that revision

### Requirement: Exact-plan approval
Execution SHALL require a current authorized approval for the exact plan revision. The system SHALL default to independent approval, honor customer-specific approval requirements, and invalidate approval when the plan, safety registry, authorization scope, or relevant configuration changes.

#### Scenario: Approved plan has expired
- **WHEN** execution is requested after plan expiry
- **THEN** the system blocks execution and requires a refreshed plan and approval

### Requirement: Fresh configuration and authorization preflight
Before applying each operation, the system SHALL re-query relevant current settings, targets, connection permissions, effective policy scope, and emergency-access state. It SHALL stop affected execution on drift or unresolved evidence rather than applying a stale plan.

#### Scenario: Customer administrator edits a policy after approval
- **WHEN** preflight detects that the relevant setting or target scope differs from the approved snapshot
- **THEN** execution is blocked and a new preview and approval are required

### Requirement: Concurrency and idempotency protection
The system SHALL prevent conflicting operations on the same tenant resource and reconcile duplicate job delivery using recorded operation identity and remote state. It SHALL use available conditional-write controls and refuse execution when required concurrency safety cannot be established.

#### Scenario: A job is delivered twice
- **WHEN** a worker receives an operation already completed and verified
- **THEN** it returns the recorded outcome without repeating the customer change

#### Scenario: Another writer changes the resource during execution
- **WHEN** a remote conditional update detects a changed resource version
- **THEN** the worker stops the operation and records a conflict instead of overwriting the external change

### Requirement: Staged lockout-sensitive changes
Identity and access changes SHALL require supported report-only or equivalent impact evaluation and pilot scope before enforcement. Changes without sufficient safety evidence SHALL remain guidance-only, even if an operator approves them.

#### Scenario: Lockout-sensitive change has no validated pilot
- **WHEN** an operator requests broad enforcement
- **THEN** the system blocks the transition and identifies the missing evaluation or pilot evidence

### Requirement: Explicit partial and uncertain outcomes
Execution SHALL record each operation's result and distinguish failed, blocked, cancelled, applied, pending verification, verified, and unknown outcomes. An uncertain write SHALL be reconciled by reading remote state before any retry; the system SHALL NOT claim atomicity across services.

#### Scenario: Connection drops after a write is sent
- **WHEN** the worker cannot determine whether the service applied the change
- **THEN** it marks the outcome unknown, reconciles the resource state, and does not blindly repeat the write

#### Scenario: A later operation fails
- **WHEN** an earlier operation succeeded but the next operation fails
- **THEN** the plan reports partial completion and preserves each operation's evidence and recovery options

### Requirement: Verification of actual effect
The system SHALL re-query changed resources and evaluate the intended control and scope after application. Eventual consistency SHALL result in bounded pending verification, not immediate success; exhausted verification SHALL remain unverified and require attention.

#### Scenario: Service accepts a change but protection is not observed
- **WHEN** reassessment does not yet show the expected effective setting or deployment
- **THEN** the result stays pending or unverified rather than compliant or successfully verified

### Requirement: Safe cancellation and emergency stop
An authorized operator SHALL be able to stop remediation per customer and globally. Workers SHALL stop before the next write, preserve any in-flight outcome, and report completed changes accurately without claiming that cancellation reverted them.

#### Scenario: Emergency stop occurs during a multi-step plan
- **WHEN** a worker observes the active stop after one operation has been applied
- **THEN** no subsequent write starts and the completed operation remains visible for verification or recovery

### Requirement: Guarded recovery
The system SHALL retain recoverable pre-change state and identify irreversible or manual-only recovery. Automated recovery SHALL require a fresh customer-scoped plan, authorization, safety checks, and approval, and SHALL NOT overwrite intervening customer changes.

#### Scenario: Recovery snapshot is older than a customer edit
- **WHEN** recovery preflight detects intervening configuration changes
- **THEN** automatic restoration is blocked and the operator receives a conflict and manual recovery guidance