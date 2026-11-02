import inventory from "../../../baselines/control-inventory.json";
import customerA from "../../../baselines/fixtures/customer-a.json";
import customerB from "../../../baselines/fixtures/customer-b.json";
import { STATUSES, type AuditEvent, type Finding, type JsonValue, type PreviewData } from "./console-model";

const titles: Record<string, string> = {
  "ENT-001": "Privileged role coverage",
  "EXO-001": "External automatic forwarding",
  "TEAM-001": "Anonymous meeting join",
  "SPO-001": "Default sharing link audience",
  "PP-001": "Environment DLP coverage",
  "INT-001": "Effective device compliance",
  "MDI-001": "Domain controller sensor health",
  "MDO-001": "Safe Links recipient coverage",
  "MDE-001": "Device tamper protection",
  "MDCA-001": "Application governance coverage",
  "XDR-001": "Licensed product integration",
  "PUR-001": "Unified audit logging",
  "AZR-001": "Storage secure transfer",
};

const reasons = [
  "Synthetic matching-state example only; not a verified live assessment.",
  "Synthetic deviation example. Manual source and scope review required.",
  "Synthetic explicit out-of-scope decision, not an inferred pass or a missing-license result.",
  "Synthetic accepted-risk example; underlying deviation remains. No real approval exists.",
  "Manual evidence required; no automated operation has been qualified.",
  "Synthetic incomplete-evidence example: denied, partial, stale or missing-license evidence cannot pass.",
];

function toJsonValue(value: unknown): JsonValue {
  if (value === null || typeof value === "string" || typeof value === "boolean" || typeof value === "number") return value;
  if (Array.isArray(value)) return value.map(toJsonValue);
  if (typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, toJsonValue(item)]));
  throw new Error("Synthetic fixture contains a non-JSON value.");
}

function deviation(expected: JsonValue): JsonValue {
  if (!expected || typeof expected !== "object" || Array.isArray(expected)) return { synthetic_deviation: true };
  const [key, value] = Object.entries(expected)[0];
  return { ...expected, [key]: typeof value === "boolean" ? !value : Array.isArray(value) ? [] : "synthetic-deviation" };
}

export function loadSyntheticData(): PreviewData {
  const customers = [customerA, customerB].map((customer) => ({ tenantId: customer.tenantId, name: customer.customerName }));
  const services = inventory.controls.map((control) => ({ ...control, expectedState: toJsonValue(control.expectedState) }));
  const findings: Finding[] = [customerA, customerB].flatMap((customer, customerIndex) => services.map((control, index) => {
    const fixture = customer.controls.find((item) => item.controlId === control.id)!;
    const scenario = (index + customerIndex) % STATUSES.length;
    const evidenceState = scenario === 5 ? (index > 5 ? "Missing license" : "Partial collection") : scenario === 4 ? (index > 4 ? "Denied / unsupported operation" : "Manual evidence") : "Synthetic scenario";
    return {
      id: `synthetic-finding-${customerIndex}-${control.id}`,
      tenantId: customer.tenantId,
      assessmentId: `synthetic-assessment-${customerIndex + 1}`,
      controlId: control.id,
      service: control.service,
      title: titles[control.id],
      status: STATUSES[scenario],
      evidenceState,
      reason: `${reasons[scenario]} Evidence condition: ${evidenceState}.`,
      severity: index % 3 === 0 ? "High" : index % 3 === 1 ? "Medium" : "Low",
      resource: fixture.resourceIds.join(", "),
      collectedAtUtc: customer.collectedAtUtc,
      observed: scenario === 1 || scenario === 3 ? deviation(control.expectedState) : scenario === 2 || scenario === 4 || scenario === 5 ? { evidence: "unknown", condition: evidenceState } : toJsonValue(fixture.observedState),
      expected: control.expectedState,
      evidenceReference: `synthetic-ui-scenario/${customerIndex + 1}/${control.id}`,
      sourceRevision: null,
      baselineVersion: "synthetic-ui-scenarios-v1",
      provenance: "Synthetic UI scenario",
    };
  }));
  const outcomes = ["Intent", "Unknown", "Verified", "Blocked", "Failed", "Partial"] as const;
  const audit: AuditEvent[] = customers.flatMap((customer, customerIndex) => outcomes.map((outcome, index) => ({
    id: `synthetic-event-${customerIndex}-${index}`,
    tenantId: customer.tenantId,
    organizationId: "synthetic-management-organization",
    timestampUtc: `2026-01-01T10:${String(index * 5).padStart(2, "0")}:00Z`,
    authorizedUserId: "synthetic-user-reviewer",
    authorizedUser: "Synthetic reviewer",
    requester: "synthetic-user-requester",
    approver: index < 3 ? "synthetic-approval-reference (not a real approval)" : "unavailable",
    executor: index < 3 ? "synthetic-workload-identity" : "unavailable",
    service: index < 3 ? "SharePoint and OneDrive" : services[index].service,
    resource: `synthetic-site-${customerIndex + 1}`,
    setting: index < 3 ? "default_link_audience" : index === 3 ? "recovery_code" : services[index].normalizedFields[0],
    previous: index === 3 ? { kind: "redacted" } : index === 4 ? { kind: "absent" } : { kind: "known", value: "anyone", sensitivity: "non-sensitive" },
    requested: index === 3 ? { kind: "redacted" } : { kind: "known", value: "organization", sensitivity: "non-sensitive" },
    verified: index === 2 ? { kind: "known", value: "organization", sensitivity: "non-sensitive" } : index === 4 ? { kind: "known", value: null, sensitivity: "non-sensitive" } : { kind: "unknown" },
    outcome,
    operationId: index < 3 ? `synthetic-operation-${customerIndex}-link` : `synthetic-operation-${customerIndex}-${index}`,
    planRevision: "synthetic-plan-v1 (not executable)",
    correlationId: `synthetic-correlation-${customerIndex}`,
    provenance: "Synthetic UI scenario",
  })));
  return { customers, services, findings, audit };
}