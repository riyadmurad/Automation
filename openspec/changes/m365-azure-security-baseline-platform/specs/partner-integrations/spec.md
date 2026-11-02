# Spec Delta

## Purpose

Use Microsoft's supported partner-management capabilities without making customer coverage or production safety dependent on unsupported APIs or conflated delegation models.

## ADDED Requirements

### Requirement: Independent customer relationship inventory
The system SHALL record Microsoft 365 Lighthouse eligibility, GDAP relationships and expiry, Azure Lighthouse delegated scopes and roles, and direct service consent as independent customer connections. Each operation SHALL select a currently authorized path for its target service.

#### Scenario: Customer has Azure delegation without Microsoft 365 consent
- **WHEN** an operator requests a Microsoft 365 assessment
- **THEN** the system identifies the missing Microsoft 365 authorization rather than treating Azure delegation as sufficient

### Requirement: Microsoft 365 Lighthouse eligibility disclosure
The system SHALL evaluate documented partner and customer eligibility against the integration's current supported requirements and show full, limited, unavailable, or unknown capability status with reasons. The MSP's CSP indirect-reseller status SHALL NOT alone establish customer eligibility.

#### Scenario: Customer fails a documented eligibility condition
- **WHEN** onboarding checks its region, size, subscription, and delegated relationship
- **THEN** unavailable Lighthouse capabilities are disclosed and direct-connector onboarding remains available

### Requirement: Production Lighthouse API boundary
Production assessment and remediation SHALL NOT depend on Microsoft 365 Lighthouse preview or beta APIs. A preview connector SHALL be disabled by default, isolated to evaluation, identify its release status and authentication limitations, and SHALL NOT feed production compliance conclusions or writes.

#### Scenario: A beta endpoint only supports delegated user access
- **WHEN** unattended production collection is configured
- **THEN** the system rejects that connector for the job and offers a supported direct-service path or an explicit coverage gap

### Requirement: Supported Lighthouse portal workflows
The system SHALL provide customer-context navigation to supported Microsoft 365 Lighthouse workflows without scraping private endpoints or assuming unsupported embedding. Navigation SHALL NOT disclose customer credentials or imply that a portal action was applied or verified by the application.

#### Scenario: Operator opens Lighthouse to complete an action
- **WHEN** the operator returns to the application
- **THEN** the finding remains unchanged until supported reassessment supplies fresh evidence

### Requirement: Azure Lighthouse delegated management
The system SHALL use Azure Lighthouse only for delegated subscriptions or resource groups and supported roles and operations. It SHALL preflight actual delegation and SHALL NOT assume Owner, custom-role, directory, data-plane, or unrestricted role-assignment permissions.

#### Scenario: Azure remediation needs an unsupported role capability
- **WHEN** the delegated authorization cannot perform the required operation
- **THEN** the system blocks the write and reports the limitation without elevating or expanding delegation

### Requirement: Direct connectors preserve coverage and standalone operation
The system SHALL support customer-authorized direct connectors independently of partner eligibility, including standalone deployment. Missing Lighthouse support SHALL NOT silently remove a customer or service from reporting.

#### Scenario: Standalone customer is not a CSP customer
- **WHEN** the customer connects its supported services directly
- **THEN** assessments and approved remediations work within those grants without requiring a partner relationship

### Requirement: Relationship expiry and revocation
The system SHALL recheck the selected access path before privileged actions and block use of expired or revoked relationships. It SHALL show which capabilities were affected and SHALL NOT silently switch to a broader access path.

#### Scenario: GDAP expires while direct consent remains valid
- **WHEN** a queued operation requires the expired GDAP relationship
- **THEN** that operation is blocked, while separately authorized direct operations remain explicitly distinguishable