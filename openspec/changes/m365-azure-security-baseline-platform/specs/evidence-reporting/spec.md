# Spec Delta

## Purpose

Provide customer-scoped, reproducible assessment evidence and detailed technical-conformance reports that explain current settings, gaps, accepted risks, and proposed changes.

## ADDED Requirements

### Requirement: Detailed finding content
Each finding SHALL include customer tenant, service, affected resources, timestamps, current and expected settings, source and baseline version, severity, rationale, evidence references, assessment status, and limitations. Proposed corrections SHALL state exact intended setting changes, prerequisites, impact, verification, and recovery limitations.

#### Scenario: Operator reviews a noncompliant setting
- **WHEN** an operator opens a finding
- **THEN** the view shows actual versus expected values and a resource-scoped change preview, or explicitly explains why only manual guidance is available

### Requirement: Evidence integrity and minimization
The system SHALL retain evidence with collection provenance and integrity metadata, restrict it to authorized customers, and redact secrets and unnecessary personal data. Tampered, missing, or expired evidence SHALL be identified and SHALL NOT be silently replaced with current data in a historical report.

#### Scenario: Evidence no longer matches its recorded integrity value
- **WHEN** a report attempts to use the evidence
- **THEN** the system marks the evidence invalid and identifies the affected findings

### Requirement: Honest summaries and coverage
Reports SHALL disclose counts and calculation denominators for each status, evaluated controls, incomplete checks, unsupported controls, and exclusions. Accepted risk SHALL NOT be counted as technical compliance, and technical baseline conformance SHALL NOT be described as regulatory certification.

#### Scenario: Some controls cannot be evaluated
- **WHEN** an assessment has compliant, noncompliant, and unable-to-assess results
- **THEN** the summary displays all counts and coverage limitations rather than presenting the successfully queried subset as total compliance

### Requirement: Expiring risk exceptions
An exception SHALL record control and resource scope, underlying measured result, rationale, owner, approver, creation time, expiry, and compensating controls. Expired exceptions SHALL cease suppressing actionable findings without deleting their history.

#### Scenario: An accepted-risk exception expires
- **WHEN** its expiry is reached
- **THEN** the affected finding becomes actionable again, the owner is notified, and reports preserve the previous acceptance record

### Requirement: Consistent report exports
The system SHALL generate detailed HTML, PDF, JSON, and CSV reports for an authorized customer and assessment. All formats SHALL use the same pinned findings and versions, record generation time and evidence freshness, and protect against active content and spreadsheet-formula injection.

#### Scenario: Operator exports a historical assessment
- **WHEN** an authorized operator generates PDF and JSON versions of the same assessment
- **THEN** both contain consistent finding statuses, baseline versions, coverage counts, limitations, and assessment identifiers

#### Scenario: Untrusted customer data contains executable content
- **WHEN** a resource name contains markup or a spreadsheet formula
- **THEN** report rendering and export treat the value as inert data

### Requirement: Efficient customer workspaces
The interface SHALL provide a scoped MSP overview and customer workspaces for connections, coverage, assessments, findings, exceptions, change plans, reports, and audit history. Customer identity and tenant ID SHALL remain visible during privileged workflows, with accessible keyboard operation and responsive layouts.

#### Scenario: Operator moves from overview to remediation
- **WHEN** an operator opens a customer's finding and change preview
- **THEN** the customer context remains explicit and filters preserve the selected assessment without changing the action scope

### Requirement: Auditable assessment and change history
The system SHALL correlate assessments, evidence, plans, approvals, executions, verification, exceptions, and operator actions without exposing credentials. History SHALL distinguish who requested, approved, executed, and verified a change.

#### Scenario: Auditor examines a completed remediation
- **WHEN** an authorized auditor opens its history
- **THEN** the system shows the originating evidence, approved plan revision, actor identities, per-operation results, and verification evidence

### Requirement: Setting-level change audit records
For every attempted setting change, the system SHALL record UTC timestamp, organization and customer tenant, authorized user ID, M365/Azure service name, resource ID, setting name, previous value, requested new value, outcome, and plan/operation correlation. Verified observed values SHALL be recorded separately. Sensitive values SHALL be explicitly redacted rather than logged.

#### Scenario: An approved setting change is verified
- **WHEN** a worker changes a customer setting and reassessment confirms its effect
- **THEN** the audit history contains the pre-write observed value, requested value, verified value, timestamps, authorized user, service and setting names, target, outcome, and matching operation identifier

#### Scenario: A setting contains sensitive material
- **WHEN** the change is audited
- **THEN** secret values are replaced with a redaction marker in stored events, views, exports, and telemetry while setting identity and outcome remain traceable

### Requirement: Trusted audit actor attribution
The system SHALL derive actor IDs from validated authentication and stored authorization context, not client-supplied names. It SHALL distinguish requesting and authorizing users, approvers, executing workload identities, and scheduled/system actors. External drift with unavailable attribution SHALL NOT be assigned to an invented user.

#### Scenario: A service identity executes a user's approved plan
- **WHEN** the worker records a customer write
- **THEN** the initiating and authorizing user identities, approval references, and actual executing service identity remain separately identifiable

#### Scenario: An assessment detects an external change
- **WHEN** no authoritative actor evidence is available
- **THEN** the system records the observation time and attribution as external or unknown, not as a platform-executed change

### Requirement: Append-only audit outcome lifecycle
The system SHALL durably record intent before a customer write and append correlated outcome and verification events afterward. Failed, blocked, cancelled, partial and uncertain changes SHALL remain distinguishable; later reconciliation SHALL NOT rewrite an earlier event or represent a requested value as an observed result.

#### Scenario: A write response is lost
- **WHEN** the service outcome cannot be established
- **THEN** audit history records the attempted new value and unknown outcome, and subsequent reconciliation appends its observed result without overwriting the attempt

### Requirement: Scoped audit search and export
Authorized users SHALL be able to search and filter change audit history by customer, UTC time range, authorized user, M365/Azure service, setting, resource, and outcome, and export JSON or CSV with the same fields and redaction. Downloads SHALL be reauthorized and restricted to permitted customer scopes.

#### Scenario: Auditor filters changes for one service
- **WHEN** an authorized auditor selects a customer, service, and time range
- **THEN** the interface and exported records contain the matching setting changes and required audit fields without exposing another customer's history