# Spec Delta

## Purpose

Maintain traceable, reviewed security-baseline releases that can be applied consistently without silently changing customer policy or conflating technical guidance with certification.

## ADDED Requirements

### Requirement: Source-backed control definitions
Each control SHALL identify its stable ID, service, source organization, source URL and revision, retrieval and review dates, rationale, expected configuration, applicability, evidence needs, and assessment and remediation support. Source-derived content SHALL remain distinguishable from organization-defined policy.

#### Scenario: Operator inspects a control
- **WHEN** an operator opens a finding's baseline definition
- **THEN** the system shows its exact source revision, expected setting, rationale, applicability, and support limitations

### Requirement: Stable initial control acceptance inventory
The initial inventory SHALL retain stable IDs, source revisions, normalized fixture fields, exact expected states, and explicit automated, manual, or unsupported status for every declared service family. Automated support SHALL remain unclaimed until its operation, permissions, licensing, and evidence path are verified.

#### Scenario: An initial control lacks verified operation support
- **WHEN** a publisher defines an initial control whose API or module operation has not been verified
- **THEN** the catalog keeps its expected state and fixture but labels collection manual or unverified and does not advertise an automated adapter

### Requirement: Source licensing and purpose
The catalog SHALL support Microsoft guidance, CISA SCuBA, appropriately licensed CIS material, and applicable SANS policy guidance without treating these sources as equivalent. Content without established usage rights SHALL NOT be redistributed or activated as a bundled baseline.

#### Scenario: Restricted benchmark content is proposed
- **WHEN** a publisher has not approved the required rights for a benchmark pack
- **THEN** the pack remains unavailable for redistribution and the catalog records the restriction

#### Scenario: Governance guidance has no automated check
- **WHEN** a SANS-derived policy requirement needs organizational evidence rather than a service setting
- **THEN** the catalog identifies it as manual review and does not invent an automated pass result

### Requirement: Reviewed and immutable baseline releases
The system SHALL monitor registered guidance sources for updates and record review freshness. New or changed controls SHALL require review and a new immutable pack version; discovering an update SHALL NOT change active customer profiles or authorize remediation.

#### Scenario: A source publishes a new recommendation
- **WHEN** the update monitor detects changed source content
- **THEN** it creates a reviewable update notice with provenance while existing assessments retain their pinned pack version

#### Scenario: Published pack content is edited
- **WHEN** a publisher attempts to overwrite an existing released pack version
- **THEN** the system rejects the overwrite and requires a new release version

### Requirement: Explicit profile inheritance
The system SHALL resolve profiles through platform baseline, MSP profile, and customer override layers, in that order. It SHALL preserve origin and approval history for every effective setting and disclose changes before activating a new profile version.

#### Scenario: Customer overrides an inherited value
- **WHEN** an authorized administrator approves a customer-specific setting
- **THEN** the effective profile shows the override and its inherited value, rationale, author, and approval

### Requirement: Conflicting guidance requires resolution
The system SHALL surface incompatible source recommendations for the same setting. It SHALL require an explicit, recorded policy choice and SHALL NOT assume that the most restrictive value is always appropriate.

#### Scenario: Two packs require incompatible sharing settings
- **WHEN** both packs are selected without an approved precedence decision
- **THEN** the system reports a profile conflict and blocks a remediation plan for that setting

### Requirement: Applicability and license constraints
The system SHALL distinguish controls that are not applicable from controls blocked by missing licenses, unavailable capabilities, missing permissions, or unsupported clouds. Unavailable evidence SHALL NOT establish compliance or non-applicability.

#### Scenario: A recommended feature requires an absent license
- **WHEN** assessment discovers that the required product entitlement is unavailable
- **THEN** the finding identifies the license limitation and retains the control in the coverage report without marking it compliant

### Requirement: Reproducible evaluation context
The system SHALL retain the baseline pack, effective profile, control revision, and evaluator version used for each assessment and plan. Later updates SHALL NOT rewrite historical findings or approved plans.

#### Scenario: An old report is opened after a pack update
- **WHEN** an operator retrieves an assessment created with an earlier release
- **THEN** its expected values and conclusions remain tied to that release and any newer baseline is clearly identified separately