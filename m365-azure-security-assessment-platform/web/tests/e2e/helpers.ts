import { test as base, expect, type Page, type TestInfo } from "@playwright/test";
import { readFile } from "node:fs/promises";

export const tenantA = "10000000-0000-0000-0000-000000000001";
export const tenantB = "20000000-0000-0000-0000-000000000002";

export const test = base.extend<{ browserEvidence: void }>({
    browserEvidence: [async ({ page }, use, testInfo) => {
        const errors: string[] = [];
        const external: string[] = [];
        page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
        page.on("console", (message) => {
            if (message.type() === "error") errors.push(`console: ${message.text()}`);
        });
        await page.route("**/*", async (route) => {
            const url = new URL(route.request().url());
            if (["127.0.0.1", "localhost"].includes(url.hostname)) await route.continue();
            else { external.push(url.origin); await route.abort(); }
        });
        page.on("response", (response) => {
            if (response.status() >= 400) errors.push(`http: ${response.status()} ${response.url()}`);
        });
        await use();
        await testInfo.attach("browser-observations", {
            body: JSON.stringify({ errors, blockedExternalOrigins: external }, null, 2),
            contentType: "application/json",
        });
        expect.soft(errors, "No render or console errors").toEqual([]);
        expect.soft(external, "No external service requests").toEqual([]);
    }, { auto: true }],
});

export async function navigate(page: Page, name: string) {
    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("button", { name: name === "Findings" ? /^Findings\s*\d*$/ : name, exact: true }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(
        name === "Overview" ? "Customer overview" : name === "Coverage" ? "Service coverage" : name === "Audit" ? "Setting-level audit" : name,
    );
}

export async function openPreview(page: Page) {
    const response = await page.goto("/");
    expect(response?.status(), "Synthetic preview must render successfully before testing interactions").toBe(200);
}

export async function captureLayout(page: Page, testInfo: TestInfo, name: string) {
    const expectedViewport = page.viewportSize()?.width;
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true });
    const layout = await page.evaluate(() => {
        const boxes: { text: string; left: number; top: number; right: number; bottom: number; node: number }[] = [];
        const modals = Array.from(document.querySelectorAll<HTMLDialogElement>("dialog:modal"));
        const activeModal = modals.at(-1);
        const modalBounds = activeModal?.getBoundingClientRect();
        const modalOverflow = activeModal ? {
            scrollWidth: activeModal.scrollWidth,
            clientWidth: activeModal.clientWidth,
            left: modalBounds!.left,
            right: modalBounds!.right,
            top: modalBounds!.top,
            bottom: modalBounds!.bottom,
        } : null;
        const walker = document.createTreeWalker(activeModal ?? document.body, NodeFilter.SHOW_TEXT);
        let node: Node | null;
        let index = 0;
        while ((node = walker.nextNode())) {
            index += 1;
            const parent = node.parentElement;
            if (!node.textContent?.trim() || !parent || parent.closest("script,style,.sr-only,nextjs-portal,details:not([open])")) continue;
            if (getComputedStyle(parent).visibility !== "visible") continue;
            const range = document.createRange();
            range.selectNodeContents(node);
            for (const rect of range.getClientRects()) {
                let left = Math.max(0, rect.left), right = Math.min(innerWidth, rect.right);
                let top = Math.max(0, rect.top), bottom = Math.min(innerHeight, rect.bottom);
                for (let ancestor: Element | null = parent; ancestor; ancestor = ancestor.parentElement) {
                    const style = getComputedStyle(ancestor);
                    const clip = ancestor.getBoundingClientRect();
                    if (style.overflowX !== "visible") { left = Math.max(left, clip.left); right = Math.min(right, clip.right); }
                    if (style.overflowY !== "visible") { top = Math.max(top, clip.top); bottom = Math.min(bottom, clip.bottom); }
                }
                if (right - left > 2 && bottom - top > 2) boxes.push({ text: node.textContent.trim().slice(0, 100), left, right, top, bottom, node: index });
            }
        }
        const overlaps: string[] = [];
        for (let first = 0; first < boxes.length; first += 1) {
            for (let second = first + 1; second < boxes.length; second += 1) {
                const left = boxes[first], right = boxes[second];
                if (left.node !== right.node && Math.min(left.right, right.right) - Math.max(left.left, right.left) > 2 && Math.min(left.bottom, right.bottom) - Math.max(left.top, right.top) > 2)
                    overlaps.push(`${left.text} <> ${right.text}`);
            }
        }
        return { viewport: innerWidth, viewportHeight: innerHeight, documentWidth: Math.max(document.documentElement.scrollWidth, document.body.scrollWidth), modalOverflow, overlaps };
    });
    await testInfo.attach(`${name}-layout`, { body: JSON.stringify({ ...layout, expectedViewport }, null, 2), contentType: "application/json" });
    expect.soft(layout.viewport, `${name}: rendered viewport differs from Playwright viewport`).toBe(expectedViewport);
    expect.soft(layout.documentWidth, `${name}: document overflow`).toBeLessThanOrEqual(expectedViewport ?? layout.viewport);
    if (layout.modalOverflow) {
        expect.soft(layout.modalOverflow.scrollWidth, `${name}: dialog horizontal overflow`).toBeLessThanOrEqual(layout.modalOverflow.clientWidth);
        expect.soft(layout.modalOverflow.left, `${name}: dialog left outside viewport`).toBeGreaterThanOrEqual(0);
        expect.soft(layout.modalOverflow.right, `${name}: dialog right outside viewport`).toBeLessThanOrEqual(layout.viewport);
        expect.soft(layout.modalOverflow.top, `${name}: dialog top outside viewport`).toBeGreaterThanOrEqual(0);
        expect.soft(layout.modalOverflow.bottom, `${name}: dialog bottom outside viewport`).toBeLessThanOrEqual(layout.viewportHeight);
    }
    expect.soft(layout.overlaps, `${name}: visible text overlap`).toEqual([]);
}

export async function downloadText(page: Page, testInfo: TestInfo, format: "CSV" | "JSON") {
    const pending = page.waitForEvent("download");
    await page.getByRole("button", { name: format, exact: true }).click();
    const download = await pending;
    expect(download.suggestedFilename()).toMatch(/^SYNTHETIC-(findings|audit)-/);
    const path = testInfo.outputPath(download.suggestedFilename());
    await download.saveAs(path);
    expect(await download.failure()).toBeNull();
    return readFile(path, "utf8");
}