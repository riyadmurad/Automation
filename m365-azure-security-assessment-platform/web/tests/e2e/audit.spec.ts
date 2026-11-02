import { expect } from "@playwright/test";
import { captureLayout, downloadText, navigate, openPreview, tenantA, tenantB, test } from "./helpers";

test("audit filters preserve outcome distinctions, redaction and customer export parity", async ({ page }, testInfo) => {
    await openPreview(page);
    await navigate(page, "Audit");
    await expect(page.locator("tbody tr")).toHaveCount(6);
    for (const outcome of ["Intent", "Unknown", "Verified", "Blocked", "Failed", "Partial"]) {
        await page.getByLabel("Status filter").selectOption(outcome);
        await expect(page.locator("tbody tr")).toHaveCount(1);
        await expect(page.locator("tbody tr")).toContainText(outcome);
    }
    await page.getByLabel("Status filter").selectOption("Verified");
    await page.getByLabel("Service filter").selectOption("SharePoint and OneDrive");
    await page.getByLabel("From UTC").fill("2026-01-01");
    await page.getByLabel("To UTC").fill("2026-01-01");
    await page.getByLabel("Authorized user", { exact: true }).fill("synthetic-user-reviewer");
    await page.getByLabel("Setting", { exact: true }).fill("default_link_audience");
    await page.getByLabel("Resource", { exact: true }).fill("synthetic-site-1");
    await expect(page.locator("tbody tr")).toHaveCount(1);
    await page.locator("tbody .row-link").click();
    const dialog = page.getByRole("dialog");
    for (const text of [tenantA, "Previous value", "Requested value", "Verified observed value", "Authorized user", "Requester", "Approver", "Executor", "Correlation", "synthetic-operation-0-link"]) await expect(dialog).toContainText(text);
    await captureLayout(page, testInfo, "audit-detail");
    await page.keyboard.press("Escape");
    const json = JSON.parse(await downloadText(page, testInfo, "JSON"));
    expect(json.records).toHaveLength(1);
    expect(json.records[0]).toMatchObject({ tenantId: tenantA, outcome: "Verified", resource: "synthetic-site-1", verified: { kind: "known", value: "organization" } });
    const csv = await downloadText(page, testInfo, "CSV");
    expect(csv).toContain("synthetic-event-0-2");
    expect(csv).not.toContain(tenantB);
    await page.getByLabel("To UTC").fill("2025-12-31");
    await expect(page.getByRole("heading", { name: "No matching records" })).toBeVisible();
    expect(JSON.parse(await downloadText(page, testInfo, "JSON")).records).toHaveLength(0);
    await page.getByRole("button", { name: "Clear filters", exact: true }).first().click();
    await page.getByLabel("Status filter").selectOption("Blocked");
    await expect(page.locator("tbody")).toContainText("[redacted]");
    const redacted = JSON.parse(await downloadText(page, testInfo, "JSON"));
    expect(redacted.records[0].previous).toEqual({ kind: "redacted" });
    expect(redacted.records[0].requested).toEqual({ kind: "redacted" });
    await page.getByLabel("Customer", { exact: true }).selectOption(tenantB);
    await navigate(page, "Audit");
    await page.getByLabel("Resource", { exact: true }).fill("synthetic-site-1");
    await expect(page.getByRole("heading", { name: "No matching records" })).toBeVisible();
    await page.getByRole("button", { name: "Clear filters", exact: true }).first().click();
    const other = JSON.parse(await downloadText(page, testInfo, "JSON"));
    expect(other.records).toHaveLength(6);
    expect(other.records.every((record: { tenantId: string; resource: string }) => record.tenantId === tenantB && record.resource === "synthetic-site-2")).toBe(true);
});