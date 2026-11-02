# Spec Delta

## Purpose

Collect current customer configurations through service-specific adapters and evaluate documented baseline controls with transparent coverage and evidence limitations across Microsoft 365 and Azure.

## ADDED Requirements

### Requirement: Declared service coverage
The system SHALL provide separately identifiable assessment modules for Entra ID, Exchange Online, Teams, SharePoint/OneDrive, Power Platform including Power Automate, Intune, Defender for Identity, Office 365, Endpoint and Cloud Apps, Defender XDR, Purview, and Azure. Each module SHALL expose a versioned per-control coverage matrix.

#### Scenario: Operator reviews available service modules
- **WHEN** an operator opens a customer's service coverage view
- **THEN** all declared service families appear with available controls, gaps, permission and license prerequisites, cloud support, evidence method, and remediation support

#### Scenario: An adapter has partial implementation
- **WHEN** some controls in a module have not been implemented
- **THEN** they are displayed as unsupported gaps and the module is not labeled fully covered

### Requirement: Initial control fixture outcomes
Initial control fixtures SHALL use the stable IDs and exact normalized expected states in the design inventory. Each result SHALL preserve source revision, fixture/schema version, support mode, evidence provenance, and a distinct unknown/manual outcome when no verified operation exists.

#### Scenario: Initial inventory fixtures are evaluated
- **WHEN** the acceptance suite evaluates the 13 initial controls ENT-001 through AZR-001
- **THEN** each fixture produces its specified state, source and support status, and an independently named test result without asserting an unverified endpoint

#### Scenario: A normalized fixture has incomplete source evidence
- **WHEN** required fields are missing, stale, denied, or partially collected
- **THEN** the affected control is unable to assess or manual review and never compliant by default

#### Scenario: ENT-001 privileged roles are covered
- **WHEN** the fixture has every privileged role covered, exclusions contained in the emergency registry, and no unknown scope
- **THEN** the normalized evaluator accepts the expected state and the result retains the recorded support mode without asserting an API operation

#### Scenario: EXO-001 external forwarding is disabled
- **WHEN** the fixture reports external auto-forwarding false and complete organization/recipient scope
- **THEN** the normalized evaluator accepts the expected state and incomplete scope remains unable to assess

#### Scenario: TEAM-001 anonymous meeting join is disabled
- **WHEN** the fixture reports anonymous meeting join false and complete policy assignment
- **THEN** the normalized evaluator accepts the expected state and missing effective assignment remains unable to assess

#### Scenario: SPO-001 default links use organization view access
- **WHEN** the fixture reports organization audience, view permission, and complete declared scope
- **THEN** the normalized evaluator accepts the expected state without inferring site-level compliance from tenant defaults

#### Scenario: PP-001 DLP covers expected environments
- **WHEN** the fixture covers every expected environment, has DLP enabled, and blocks every prohibited connector pair
- **THEN** the normalized evaluator accepts the expected state and an untested connector pair remains manual or unable to assess

#### Scenario: INT-001 expected devices are compliant
- **WHEN** the fixture's compliant-device set equals the approved expected-device set and pending, failed, and unassigned sets are empty
- **THEN** the normalized evaluator accepts the expected state and missing inventory provenance prevents a pass

#### Scenario: MDI-001 expected domain controllers have healthy sensors
- **WHEN** the healthy-sensor set equals the expected domain-controller set and no sensor state is unknown
- **THEN** the normalized evaluator accepts the expected state and inaccessible sensor evidence remains manual or unable to assess

#### Scenario: MDO-001 Safe Links covers expected recipients
- **WHEN** Safe Links is enabled, click-through is disabled, and covered recipients equal expected recipients
- **THEN** the normalized evaluator accepts the expected state and missing license or incomplete precedence evidence remains visible

#### Scenario: MDE-001 expected devices have tamper protection
- **WHEN** the tamper-protected-device set equals the expected-device set and no device state is unknown
- **THEN** the normalized evaluator accepts the expected state and incomplete inventory cannot produce a coverage pass

#### Scenario: MDCA-001 governance policies cover expected apps
- **WHEN** every expected app is covered by at least one enabled governance policy
- **THEN** the normalized evaluator accepts the expected state and a connected app without policy coverage remains noncompliant or unable to assess

#### Scenario: XDR-001 expected licensed products are integrated
- **WHEN** integrated-product IDs equal expected licensed-product IDs and no product state is unknown
- **THEN** the normalized evaluator accepts the expected state and incident data is not substituted for configuration evidence

#### Scenario: PUR-001 unified audit logging is enabled
- **WHEN** the fixture reports unified audit logging enabled with fresh evidence
- **THEN** the normalized evaluator accepts the expected state and stale or unavailable evidence remains unable to assess

#### Scenario: AZR-001 expected storage accounts require secure transfer
- **WHEN** secure-transfer-required account IDs equal the complete expected storage-account set
- **THEN** the normalized evaluator accepts the expected state and unknown ARM inventory scope prevents a pass

### Requirement: Read-only assessment execution
Assessment SHALL query current service configurations using read-authorized connections without changing customer settings. Evidence SHALL include tenant, service, resource identifiers, collection time, source operation, and adapter version.

#### Scenario: Assessment requests a setting change
- **WHEN** an assessment adapter attempts a customer write operation
- **THEN** the operation is blocked and recorded as an assessment error

### Requirement: Effective configuration evaluation
The system SHALL evaluate applicable scope, inclusions, exclusions, precedence, assignments, and deployment coverage where evidence is available. The existence of a policy SHALL NOT alone establish that its intended targets are protected.

#### Scenario: Intune profile exists without effective assignment
- **WHEN** a secure deployment profile is present but its intended devices are unassigned, excluded, pending, or in error
- **THEN** the assessment reports the actual coverage and deployment states rather than a blanket compliant result

#### Scenario: Conditional Access policies overlap
- **WHEN** applicable Entra policies have overlapping inclusions and exclusions
- **THEN** assessment evaluates their combined supported scope and flags unresolved scope as unable to assess

### Requirement: Collaboration and messaging assessment
The system SHALL assess supported security settings for Exchange Online, Defender for Office 365, Teams, SharePoint, and OneDrive, including external access, sharing, mail protection, and effective policy scope. It SHALL preserve resource-level differences.

#### Scenario: Exchange and Office protection cover different recipients
- **WHEN** mail protection policies leave intended recipients outside their effective scope
- **THEN** the report identifies the affected scope and the configuration changes needed to close the gap

#### Scenario: Teams and site-level settings differ
- **WHEN** Teams federation or guest settings and SharePoint or OneDrive sharing settings differ from their baseline
- **THEN** each service produces separate evidence-backed findings without assuming one policy configures the other

### Requirement: Power Platform governance assessment
The system SHALL assess supported environment, DLP, connector, sharing, and ownership controls for Power Platform and Power Automate. An inventory of flows alone SHALL NOT count as governance assessment.

#### Scenario: A flow uses a restricted connector combination
- **WHEN** available environment and DLP evidence shows that a flow's connector usage violates the selected baseline
- **THEN** the system reports the environment, affected flow references, policy evidence, and supported corrective action

### Requirement: Defender product-specific coverage
The system SHALL assess supported product configuration and coverage independently for Defender for Identity, Office 365, Endpoint, and Cloud Apps, and assess shared XDR integration separately. Findings SHALL distinguish configuration, onboarding or sensor health, and security incidents.

#### Scenario: Identity sensor evidence is unavailable
- **WHEN** required Defender for Identity sensor or on-premises coverage data cannot be retrieved
- **THEN** the control remains unable to assess or manual review instead of being inferred compliant from product availability

#### Scenario: Endpoint protection is configured but devices are missing
- **WHEN** endpoint policy configuration meets baseline but onboarding or deployment evidence shows uncovered devices
- **THEN** the system reports configuration conformance separately from protection coverage

#### Scenario: Cloud Apps and XDR integration are assessed
- **WHEN** connected-app, governance, session-control, or cross-product integration evidence is collected
- **THEN** findings identify the originating product, effective scope, and any unsupported configuration areas

### Requirement: Purview and Azure assessment
The system SHALL assess supported Purview audit, DLP, information-protection and retention settings, and Azure resource, policy, identity-access, network, logging and Defender for Cloud settings. Existing score and recommendation feeds SHALL supplement, not replace, direct control evidence.

#### Scenario: Secure Score is high but direct evidence fails
- **WHEN** a direct check shows a baseline deviation despite a favorable Microsoft score
- **THEN** the system reports the deviation and labels the score as a separate posture signal

#### Scenario: Purview scope differs from expected scope
- **WHEN** a DLP or audit configuration does not cover the selected baseline's intended locations
- **THEN** the report identifies the uncovered scope and available evidence without declaring regulatory certification

### Requirement: Honest assessment outcomes
Each control SHALL produce compliant, noncompliant, not applicable, exception, manual review, or unable to assess status with a reason and evidence references. Incomplete queries, stale data, API errors, missing permissions, and throttling exhaustion SHALL NOT produce a pass.

#### Scenario: Collection stops after one page
- **WHEN** a paginated source fails before all relevant resources are retrieved
- **THEN** the assessment marks affected coverage incomplete and does not treat the retrieved subset as the complete tenant

### Requirement: Repeatable scheduled assessments
The system SHALL support on-demand and scheduled assessments pinned to an effective profile and scope, and distinguish detected customer drift from changes caused by newer baseline or evaluator versions.

#### Scenario: Baseline revision changes between runs
- **WHEN** two assessments use different expected configurations
- **THEN** their comparison separates baseline changes from actual customer-setting changes