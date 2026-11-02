import { expect } from "@playwright/test";
import { captureLayout, navigate, openPreview, tenantA, tenantB, test } from "./helpers";

test("five screens render without overflow or text overlap; tenant switch resets state", async ({ page }, testInfo) => {
  await openPreview(page);
  await expect(page.getByLabel("Customer", { exact: true })).toHaveValue(tenantA);
  for (const name of ["Overview", "Assessments", "Findings", "Coverage", "Audit"]) {
    await navigate(page, name);
    await expect(page.getByRole("region", { name: "Current customer" })).toContainText(tenantA);
    await captureLayout(page, testInfo, name.toLowerCase());
  }
  await page.getByLabel("Status filter").selectOption("Unknown");
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await page.locator("tbody .row-link").click();
  await expect(page.getByRole("dialog")).toContainText(tenantA);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByLabel("Customer", { exact: true }).selectOption(tenantB);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Customer overview");
  await navigate(page, "Audit");
  await expect(page.getByLabel("Status filter")).toHaveValue("");
  await expect(page.locator("tbody tr")).toHaveCount(6);
  await expect(page.locator("tbody")).toContainText("default_link_audience");
  await page.reload();
  await expect(page.getByLabel("Customer", { exact: true })).toHaveValue(tenantB);
  await page.evaluate(() => localStorage.setItem("security-assessment.synthetic-tenant.v1", "unassigned-tenant"));
  await page.reload();
  await expect(page.getByLabel("Customer", { exact: true })).toHaveValue(tenantA);
});