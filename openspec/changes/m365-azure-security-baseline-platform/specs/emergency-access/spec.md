# Spec Delta

## Purpose

Protect customer-confirmed emergency-access accounts from lockout-prone automation while preserving their security assessment, monitoring, and explicit exception history.

## ADDED Requirements

### Requirement: Customer-confirmed emergency account registry
The system SHALL maintain a versioned per-customer registry of emergency accounts using immutable directory object IDs, customer confirmation, owner, intended recovery role, control-specific exclusion rationale, and review and readiness records. Display names or naming conventions SHALL NOT establish emergency status.

#### Scenario: An account resembles a break-glass naming convention
- **WHEN** assessment discovers the account without an approved registry entry
- **THEN** it is not automatically excluded and the operator is asked to confirm emergency-account designation

#### Scenario: A registered account is renamed
- **WHEN** its directory object ID remains unchanged
- **THEN** protection remains attached to that object ID and the displayed name is refreshed

### Requirement: Control-specific lockout exclusions
Plans affecting authentication or access SHALL preserve verified emergency-account exclusions for applicable lockout-prone controls. The system SHALL NOT exempt emergency accounts from all controls or assume that excluding one policy excludes overlapping policies.

#### Scenario: Emergency account remains targeted by another policy
- **WHEN** preflight detects another applicable policy that defeats the planned recovery path
- **THEN** enforcement is blocked and the conflicting effective scope is shown

### Requirement: Fail-closed emergency-access preflight
Before a lockout-sensitive change, the system SHALL verify registered accounts resolve to the correct tenant, are enabled, retain required access, and have a current customer-confirmed recovery-readiness record. Missing or unresolved evidence SHALL block the affected change; account existence alone SHALL NOT establish safe recovery.

#### Scenario: Registered emergency account has been deleted or disabled
- **WHEN** preflight checks the approved plan
- **THEN** the system blocks the affected change and identifies the account's invalid state

#### Scenario: Recovery readiness cannot be established
- **WHEN** the registry is empty or the required review or effective-scope evidence is unavailable
- **THEN** the plan cannot proceed to lockout-sensitive enforcement

### Requirement: Protected account and registry changes
Ordinary remediation SHALL NOT disable, delete, remove recovery access from, or change authentication methods of registered emergency accounts. Registry changes SHALL require authorized customer-scoped approval and SHALL invalidate dependent plans and approvals.

#### Scenario: Bulk identity remediation includes an emergency account
- **WHEN** an operation would alter its protected recovery properties
- **THEN** the system removes the operation from executable scope and reports the protection reason

#### Scenario: Registry changes after plan approval
- **WHEN** an emergency account or its approved exclusions change
- **THEN** affected plans require fresh safety evaluation and approval

### Requirement: Emergency accounts remain assessed
The system SHALL assess emergency accounts against applicable security controls and report intentional exclusions as explicit exceptions with owners and review dates. It SHALL preserve the underlying measured result rather than hiding excluded accounts from reports.

#### Scenario: An exclusion satisfies availability but creates a security exception
- **WHEN** the assessment evaluates the registered account
- **THEN** both the recovery rationale and the remaining security exposure appear in the report

### Requirement: Emergency-access monitoring visibility
The system SHALL expose available emergency-account sign-in and configuration-change monitoring status, alert routing, last review, and customer test records. It SHALL distinguish configured monitoring from verified alert delivery and disclose unavailable telemetry.

#### Scenario: Sign-in monitoring cannot be read
- **WHEN** required telemetry permissions or licenses are unavailable
- **THEN** the system reports monitoring coverage as unknown rather than healthy

### Requirement: No automated emergency credential testing
The system SHALL NOT store emergency-account passwords, recovery codes, or tokens, or perform unattended emergency sign-in tests. Recovery exercises SHALL be customer-controlled and recorded as evidence without capturing secrets; exclusions SHALL NOT be described as a guarantee against lockout.

#### Scenario: Customer records a recovery exercise
- **WHEN** an authorized reviewer records its outcome and evidence reference
- **THEN** the system retains the review metadata without requesting emergency credentials