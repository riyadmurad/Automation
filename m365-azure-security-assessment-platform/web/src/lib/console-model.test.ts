import { describe, expect, it } from "vitest";
import { auditValueText, createExport, csvCell, filterAudit, filterFindings, redact, STATUSES, type AuditValue } from "./console-model";
import { loadSyntheticData } from "./synthetic-data";
import inventory from "../../../baselines/control-inventory.json";

const data = loadSyntheticData();
const tenantA = data.customers[0].tenantId;
const tenantB = data.customers[1].tenantId;

describe("tenant-scoped synthetic data", () => {
  it("retains exactly the inventory's 13 service families", () => {
    expect(data.services.map((service) => service.id)).toEqual(inventory.controls.map((control) => control.id));
    expect(new Set(data.services.map((service) => service.service)).size).toBe(13);
    expect(data.services.every((service) => service.supportMode === "manual/unverified")).toBe(true);
  });
  it("shows all six distinct illustrative statuses", () => {
    expect(new Set(filterFindings(data.findings, tenantA).map((row) => row.status))).toEqual(new Set(STATUSES));
  });
  it("isolates findings even without a filter", () => {
    expect(filterFindings(data.findings, tenantA)).toHaveLength(13);
    expect(filterFindings(data.findings, tenantA).every((row) => row.tenantId !== tenantB)).toBe(true);
    expect(filterFindings(data.findings, "unassigned")).toEqual([]);
  });
  it("isolates audit and combines UTC, setting, user, resource and outcome filters", () => {
    const rows = filterAudit(data.audit, tenantA, { from: "2026-01-01", to: "2026-01-01", setting: "default_link", user: "reviewer", resource: "site-1", status: "Unknown" });
    expect(rows).toHaveLength(1);
    expect(rows[0].tenantId).toBe(tenantA);
    expect(filterAudit(data.audit, tenantA, { from: "2026-01-02" })).toEqual([]);
  });
  it.each(["csv", "json"] as const)("exports only the selected tenant in %s with provenance", (format) => {
    const result = createExport("audit", format, tenantA, data, { status: "Unknown" });
    expect(result).not.toContain(tenantB);
    expect(result).toContain("SYNTHETIC PREVIEW - NOT LIVE EVIDENCE");
    expect(result).toContain("Unknown");
    expect(result).not.toContain('"Verified"');
  });
  it("rejects exports for an unknown tenant", () => {
    expect(() => createExport("findings", "json", "unknown", data, {})).toThrow("Unknown synthetic tenant");
  });
});

describe("safe exports", () => {
  it.each(["=1+1", "+cmd", "-1+2", "@SUM(A1)", "  =HYPERLINK(1)", "\t=1", "\r=1", "\n@cmd", "\u0000=1"])("neutralizes formula %j", (input) => {
    expect(csvCell(input).startsWith('"\'')).toBe(true);
  });
  it("quotes delimiters, newlines and embedded quotes", () => {
    expect(csvCell('a,"b"\nc')).toBe('"a,""b""\nc"');
  });
  it("redacts nested sensitive keys", () => {
    expect(redact({ observed: { accessToken: "never-export", password: "never-export", private_key: "never-export", recoveryCode: "never-export" } })).toEqual({ observed: { accessToken: "[redacted]", password: "[redacted]", private_key: "[redacted]", recoveryCode: "[redacted]" } });
  });
  it.each(["accessToken", "access_key", "credential", "PRIVATE__KEY", "recovery-_.code"])("normalizes sensitive setting %s across table, drawer and exports", (setting) => {
    const value = { kind: "known" as const, value: "opaque-setting-material", sensitivity: "non-sensitive" as const };
    const unsafeData = { ...data, audit: [{ ...data.audit[0], setting, previous: value, requested: value, verified: value }] };
    const row = filterAudit(unsafeData.audit, tenantA)[0];
    for (const field of ["previous", "requested", "verified"] as const) {
      expect(row[field]).toEqual({ kind: "redacted" });
      expect(auditValueText(row[field])).toBe("[redacted]");
    }
    expect(JSON.stringify(redact(row))).not.toContain(value.value);
    for (const format of ["csv", "json"] as const) expect(createExport("audit", format, tenantA, unsafeData, {})).not.toContain(value.value);
    expect(value.value).toBe("opaque-setting-material");
  });
  it.each(["accessToken", "access_key", "credential"])("recursively redacts opaque nested %s before table stringify, drawer and exports", (key) => {
    const secret = "opaque-nested-material";
    const value = { kind: "known" as const, sensitivity: "non-sensitive" as const, value: { levels: [{ safe: "retained", nested: { [key]: secret } }] } };
    const unsafeData = { ...data, audit: [{ ...data.audit[0], previous: value, requested: value, verified: value }] };
    const row = filterAudit(unsafeData.audit, tenantA)[0];
    expect(auditValueText(value)).not.toContain(secret);
    expect(auditValueText(value)).toContain("retained");
    for (const field of ["previous", "requested", "verified"] as const) {
      expect(auditValueText(row[field])).not.toContain(secret);
      expect(auditValueText(row[field])).toContain("[redacted]");
    }
    expect(JSON.stringify(redact(row))).not.toContain(secret);
    for (const format of ["csv", "json"] as const) expect(createExport("audit", format, tenantA, unsafeData, {})).not.toContain(secret);
    expect(value.value.levels[0].nested[key]).toBe(secret);
  });
  it.each(["previous", "requested", "verified"] as const)("fails closed for missing %s sensitivity across table, drawer and exports", (field) => {
    const secret = "opaque-unclassified-material";
    const value = { kind: "known", value: secret } as AuditValue;
    const unsafeData = { ...data, audit: [{ ...data.audit[0], [field]: value }] };
    const row = filterAudit(unsafeData.audit, tenantA)[0];
    expect(row[field]).toEqual({ kind: "redacted" });
    expect(auditValueText(value)).toBe("[redacted]");
    expect(auditValueText(row[field])).toBe("[redacted]");
    expect(redact(value)).toEqual({ kind: "redacted" });
    expect(JSON.stringify(redact(row))).not.toContain(secret);
    for (const format of ["csv", "json"] as const) expect(createExport("audit", format, tenantA, unsafeData, {})).not.toContain(secret);
  });
  it.each(["csv", "json"] as const)("redacts all old/requested/verified values of sensitive settings in %s", (format) => {
    const unsafe = { ...data.audit[0], setting: "access_token", previous: { kind: "known" as const, value: "never-export-old", sensitivity: "non-sensitive" as const }, requested: { kind: "known" as const, value: "never-export-new", sensitivity: "non-sensitive" as const }, verified: { kind: "known" as const, value: "never-export-observed", sensitivity: "non-sensitive" as const } };
    const unsafeData = { ...data, audit: [unsafe] };
    const rows = filterAudit(unsafeData.audit, tenantA);
    expect(rows[0].previous.kind).toBe("redacted");
    expect(createExport("audit", format, tenantA, unsafeData, {})).not.toContain("never-export");
  });
  it.each(["csv", "json"] as const)("redacts opaque apiKey values from explicit sensitivity metadata in %s", (format) => {
    const secret = "opaque-api-key-value";
    const value = { kind: "known" as const, value: secret, sensitivity: "sensitive" as const };
    const unsafe = { ...data.audit[0], setting: "apiKey", previous: value, requested: value, verified: value };
    const unsafeData = { ...data, audit: [unsafe] };
    const rows = filterAudit(unsafeData.audit, tenantA);
    expect(rows[0].previous.kind).toBe("redacted");
    expect(auditValueText(value)).toBe("[redacted]");
    expect(createExport("audit", format, tenantA, unsafeData, {})).not.toContain(secret);
  });
  it("preserves null, absent, unknown and redacted distinctly", () => {
    const result = createExport("audit", "json", tenantA, data, {});
    for (const marker of ['"kind": "absent"', '"kind": "unknown"', '"kind": "redacted"', '"value": null']) expect(result).toContain(marker);
  });
});