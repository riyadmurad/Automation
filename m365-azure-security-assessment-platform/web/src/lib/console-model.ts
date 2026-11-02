export const STATUSES = ["Compliant", "Noncompliant", "Not applicable", "Exception", "Manual review", "Unable to assess"] as const;
export type ControlStatus = (typeof STATUSES)[number];
export type JsonValue = null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue };
export type AuditValue = { kind: "known"; value: JsonValue; sensitivity: "sensitive" | "non-sensitive" } | { kind: "unknown" | "absent" | "redacted" | "unavailable" };

export interface Customer {
  tenantId: string;
  name: string;
}

export interface ServiceControl {
  id: string;
  service: string;
  supportMode: string;
  expectedState: JsonValue;
  sourceUrl: string;
  permissionPrerequisites: string[];
  licensePrerequisites: string[];
  runtimePrerequisites: string[];
}

export interface Finding {
  id: string;
  tenantId: string;
  assessmentId: string;
  controlId: string;
  service: string;
  title: string;
  status: ControlStatus;
  evidenceState: string;
  reason: string;
  severity: "High" | "Medium" | "Low";
  resource: string;
  collectedAtUtc: string;
  observed: JsonValue;
  expected: JsonValue;
  evidenceReference: string;
  sourceRevision: null;
  baselineVersion: string;
  provenance: "Synthetic UI scenario";
}

export interface AuditEvent {
  id: string;
  tenantId: string;
  organizationId: string;
  timestampUtc: string;
  authorizedUserId: string;
  authorizedUser: string;
  requester: string;
  approver: string;
  executor: string;
  service: string;
  resource: string;
  setting: string;
  previous: AuditValue;
  requested: AuditValue;
  verified: AuditValue;
  outcome: "Intent" | "Unknown" | "Verified" | "Blocked" | "Failed" | "Partial";
  operationId: string;
  planRevision: string;
  correlationId: string;
  provenance: "Synthetic UI scenario";
}

export interface PreviewData {
  customers: Customer[];
  services: ServiceControl[];
  findings: Finding[];
  audit: AuditEvent[];
}

export interface Filters {
  search?: string;
  service?: string;
  status?: string;
  user?: string;
  setting?: string;
  resource?: string;
  from?: string;
  to?: string;
}

export function filterFindings(rows: Finding[], tenantId: string, filters: Filters = {}): Finding[] {
  const search = (filters.search ?? "").toLowerCase();
  return rows.filter((row) => row.tenantId === tenantId &&
    (!filters.service || row.service === filters.service) &&
    (!filters.status || row.status === filters.status) &&
    `${row.controlId} ${row.title} ${row.resource} ${row.reason}`.toLowerCase().includes(search));
}

function isSensitiveName(name: string): boolean {
  return /password|secret|token|privatekey|recoverycode|credential|apikey|accesskey/i.test(name.replace(/[^\p{L}\p{N}]/gu, ""));
}

export function filterAudit(rows: AuditEvent[], tenantId: string, filters: Filters = {}): AuditEvent[] {
  const contains = (value: string, term?: string) => value.toLowerCase().includes((term ?? "").toLowerCase());
  const from = filters.from ? Date.parse(`${filters.from}T00:00:00Z`) : -Infinity;
  const to = filters.to ? Date.parse(`${filters.to}T23:59:59.999Z`) : Infinity;
  return rows.filter((row) => row.tenantId === tenantId &&
    (!filters.service || row.service === filters.service) &&
    (!filters.status || row.outcome === filters.status) &&
    contains(`${row.authorizedUser} ${row.authorizedUserId}`, filters.user) &&
    contains(row.setting, filters.setting) && contains(row.resource, filters.resource) &&
    contains(`${row.id} ${row.service} ${row.setting} ${row.resource} ${row.outcome}`, filters.search) &&
    Date.parse(row.timestampUtc) >= from && Date.parse(row.timestampUtc) <= to)
    .map((row) => {
      const sensitiveSetting = isSensitiveName(row.setting);
      const redactValue = (value: AuditValue): AuditValue => value.kind === "known" && (sensitiveSetting || value.sensitivity !== "non-sensitive")
        ? { kind: "redacted" }
        : redact(value) as AuditValue;
      return { ...row, previous: redactValue(row.previous), requested: redactValue(row.requested), verified: redactValue(row.verified) };
    });
}

export function auditValueText(value: AuditValue): string {
  return value.kind === "known" && value.sensitivity === "non-sensitive" ? JSON.stringify(redact(value.value)) : `[${value.kind === "known" ? "redacted" : value.kind}]`;
}

export function redact(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(redact);
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    if (record.kind === "known" && record.sensitivity !== "non-sensitive") return { kind: "redacted" };
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [
      key, isSensitiveName(key) ? "[redacted]" : redact(item),
    ]));
  }
  return value;
}

export function csvCell(value: unknown): string {
  const text = typeof value === "string" ? value : JSON.stringify(redact(value)) ?? "";
  let firstNonWhitespace = 0;
  while (firstNonWhitespace < text.length && (text.charCodeAt(firstNonWhitespace) <= 0x1f || text[firstNonWhitespace].trim() === "")) firstNonWhitespace += 1;
  const startsWithFormula = /^[=+\-@]/.test(text[firstNonWhitespace] ?? "");
  const startsWithLineBreak = [9, 10, 13].includes(text.charCodeAt(0));
  const safe = startsWithFormula || startsWithLineBreak ? `'${text}` : text;
  return `"${safe.replaceAll('"', '""')}"`;
}

export function createExport(
  kind: "findings" | "audit",
  format: "csv" | "json",
  tenantId: string,
  data: PreviewData,
  filters: Filters,
  generatedAtUtc = new Date().toISOString(),
): string {
  if (!data.customers.some((customer) => customer.tenantId === tenantId)) throw new Error("Unknown synthetic tenant scope.");
  const records = kind === "audit" ? filterAudit(data.audit, tenantId, filters) : filterFindings(data.findings, tenantId, filters);
  const provenance = { source: "SYNTHETIC PREVIEW - NOT LIVE EVIDENCE", schemaVersion: "1.0", tenantId, generatedAtUtc, filters, sourceRevision: "unverified", writesEnabled: false };
  if (format === "json") return JSON.stringify(redact({ provenance, records }), null, 2);
  const rows = records.map((record) => ({
    exportProvenance: provenance.source,
    exportSchemaVersion: provenance.schemaVersion,
    exportGeneratedAtUtc: generatedAtUtc,
    exportSourceRevision: provenance.sourceRevision,
    exportFilters: filters,
    ...record,
  }));
  const columns = rows.length ? Object.keys(rows[0]) : ["exportProvenance", "tenantId", "exportGeneratedAtUtc", "recordCount"];
  const output = rows.length ? rows : [{ exportProvenance: provenance.source, tenantId, exportGeneratedAtUtc: generatedAtUtc, recordCount: 0 }];
  return [columns.map(csvCell).join(","), ...output.map((row) => columns.map((key) => csvCell((redact(row) as Record<string, unknown>)[key])).join(","))].join("\r\n");
}