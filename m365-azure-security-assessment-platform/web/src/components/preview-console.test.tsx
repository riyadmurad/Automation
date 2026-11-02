import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import PreviewConsole from "./preview-console";
import { loadSyntheticData } from "@/lib/synthetic-data";
import type { AuditValue } from "@/lib/console-model";

const data = loadSyntheticData();
const tenantA = data.customers[0].tenantId;
const tenantB = data.customers[1].tenantId;

beforeEach(() => { localStorage.setItem("security-assessment.synthetic-tenant.v1", tenantA); });

describe("synthetic operator workspace", () => {
    it("starts with explicit provenance, tenant identity and no live execution controls", () => {
        render(<PreviewConsole data={data} />);
        expect(screen.getByRole("heading", { name: "Customer overview" })).toBeVisible();
        expect(screen.getByText(tenantA)).toBeVisible();
        expect(screen.getByText("Synthetic preview")).toBeVisible();
        expect(screen.queryByRole("button", { name: /connect|remediate|approve|start scan/i })).toBeNull();
    });

    it("navigates through every view and exposes exactly 13 coverage rows", async () => {
        const user = userEvent.setup();
        render(<PreviewConsole data={data} />);
        for (const [button, heading] of [["Assessments", "Assessments"], ["Findings", "Findings"], ["Coverage", "Service coverage"], ["Audit", "Setting-level audit"]]) {
            await user.click(screen.getByRole("button", { name: new RegExp(`^${button}`) }));
            expect(screen.getByRole("heading", { name: heading })).toBeVisible();
        }
        await user.click(screen.getByRole("button", { name: "Coverage" }));
        expect(screen.getAllByRole("row")).toHaveLength(14);
        expect(screen.getAllByText("Manual / unverified")).toHaveLength(13);
    });

    it("supports search, status filtering, empty state and reset", async () => {
        const user = userEvent.setup();
        render(<PreviewConsole data={data} />);
        await user.click(screen.getByRole("button", { name: /^Findings/ }));
        await user.selectOptions(screen.getByLabelText("Status filter"), "Noncompliant");
        expect(screen.getByText("2 of 13 fixture findings")).toBeVisible();
        await user.type(screen.getByRole("textbox", { name: "Search records" }), "no-such-resource");
        expect(screen.getByRole("heading", { name: "No matching records" })).toBeVisible();
        await user.click(screen.getAllByRole("button", { name: "Clear filters" })[0]);
        expect(screen.getByText("13 of 13 fixture findings")).toBeVisible();
    });

    it("opens and closes evidence detail with current versus expected values", async () => {
        const user = userEvent.setup();
        render(<PreviewConsole data={data} />);
        await user.click(screen.getByRole("button", { name: /^Findings/ }));
        await user.click(screen.getByRole("button", { name: "Open EXO-001" }));
        const dialog = screen.getByRole("dialog");
        expect(within(dialog).getByRole("heading", { name: "Observed fixture value" })).toBeVisible();
        expect(within(dialog).getByRole("heading", { name: "Expected fixture value" })).toBeVisible();
        expect(within(dialog).getByText("Unverified / activation blocked")).toBeVisible();
        await user.click(within(dialog).getByRole("button", { name: "Close detail" }));
        expect(screen.queryByRole("dialog")).toBeNull();
    });

    it("persists only the synthetic tenant ID and clears stale drawers and filters on tenant changes", async () => {
        const user = userEvent.setup();
        render(<PreviewConsole data={data} />);
        await user.click(screen.getByRole("button", { name: /^Findings/ }));
        await user.click(screen.getByRole("button", { name: "Open ENT-001" }));
        fireEvent.change(screen.getByRole("combobox", { name: "Customer" }), { target: { value: tenantB } });
        expect(screen.queryByRole("dialog")).toBeNull();
        expect(screen.queryByText(tenantA)).toBeNull();
        expect(screen.getByText(tenantB)).toBeVisible();
        expect(localStorage.getItem("security-assessment.synthetic-tenant.v1")).toBe(tenantB);
        expect(screen.getByRole("heading", { name: "Customer overview" })).toBeVisible();
    });

    it("combines audit filters and keeps old/requested/verified values distinct", async () => {
        const user = userEvent.setup();
        render(<PreviewConsole data={data} />);
        await user.click(screen.getByRole("button", { name: "Audit" }));
        await user.selectOptions(screen.getByLabelText("Status filter"), "Unknown");
        await user.type(screen.getByLabelText("Setting"), "default_link");
        await user.type(screen.getByLabelText("Authorized user"), "reviewer");
        fireEvent.change(screen.getByLabelText("From UTC"), { target: { value: "2026-01-01" } });
        expect(screen.getByText("1 matching synthetic events")).toBeVisible();
        await user.click(screen.getByRole("button", { name: "Open synthetic-event-0-1" }));
        const dialog = screen.getByRole("dialog");
        expect(within(dialog).getByRole("heading", { name: "Previous value" })).toBeVisible();
        expect(within(dialog).getByRole("heading", { name: "Requested value" })).toBeVisible();
        expect(within(dialog).getByRole("heading", { name: "Verified observed value" })).toBeVisible();
        expect(within(dialog).getByText("synthetic-operation-0-link")).toBeVisible();
    });

    it.each(["previous", "requested", "verified"] as const)("hides unclassified %s values in the audit table and drawer", async (field) => {
        const user = userEvent.setup();
        const secret = "opaque-unclassified-material";
        const value = { kind: "known", value: secret } as AuditValue;
        const row = { ...data.audit[0], [field]: value };
        render(<PreviewConsole data={{ ...data, audit: [row] }} />);
        await user.click(screen.getByRole("button", { name: "Audit" }));
        expect(screen.getByRole("table")).not.toHaveTextContent(secret);
        expect(within(screen.getByRole("table")).getByText("[redacted]")).toBeVisible();
        await user.click(screen.getByRole("button", { name: `Open ${row.id}` }));
        expect(screen.getByRole("dialog")).not.toHaveTextContent(secret);
        expect(screen.getByRole("dialog")).toHaveTextContent('"kind": "redacted"');
    });

    it.each(["access_key", "credential"])("hides %s settings in all audit value columns and the drawer", async (setting) => {
        const user = userEvent.setup();
        const secret = "opaque-setting-material";
        const value = { kind: "known" as const, sensitivity: "non-sensitive" as const, value: secret };
        const row = { ...data.audit[0], setting, previous: value, requested: value, verified: value };
        render(<PreviewConsole data={{ ...data, audit: [row] }} />);
        await user.click(screen.getByRole("button", { name: "Audit" }));
        expect(screen.getByRole("table")).not.toHaveTextContent(secret);
        expect(within(screen.getByRole("table")).getAllByText("[redacted]")).toHaveLength(3);
        await user.click(screen.getByRole("button", { name: `Open ${row.id}` }));
        expect(screen.getByRole("dialog")).not.toHaveTextContent(secret);
        expect(within(screen.getByRole("dialog")).getAllByText(/"kind": "redacted"/)).toHaveLength(3);
    });

    it("redacts opaque nested credential keys in every audit column and the drawer", async () => {
        const user = userEvent.setup();
        const secret = "opaque-nested-material";
        const value = { kind: "known" as const, sensitivity: "non-sensitive" as const, value: { levels: [{ safe: "retained", nested: { accessToken: secret, access_key: secret, credential: secret } }] } };
        const row = { ...data.audit[0], previous: value, requested: value, verified: value };
        render(<PreviewConsole data={{ ...data, audit: [row] }} />);
        await user.click(screen.getByRole("button", { name: "Audit" }));
        expect(screen.getByRole("table")).not.toHaveTextContent(secret);
        expect(within(screen.getByRole("table")).getAllByText(/retained/)).toHaveLength(3);
        await user.click(screen.getByRole("button", { name: `Open ${row.id}` }));
        expect(screen.getByRole("dialog")).not.toHaveTextContent(secret);
        expect(within(screen.getByRole("dialog")).getAllByText(/"accessToken": "\[redacted\]"/)).toHaveLength(3);
    });

    it("downloads scoped synthetic files and surfaces recoverable export errors", async () => {
        const user = userEvent.setup();
        const createUrl = vi.fn(() => "blob:synthetic");
        Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createUrl });
        Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
        const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => { });
        render(<PreviewConsole data={data} />);
        await user.click(screen.getByRole("button", { name: "Audit" }));
        await user.click(screen.getByRole("button", { name: "JSON" }));
        expect(createUrl).toHaveBeenCalledOnce();
        expect(click).toHaveBeenCalledOnce();
        expect(screen.getByRole("status")).toHaveTextContent("Synthetic JSON export prepared for Synthetic Customer A");
        createUrl.mockImplementation(() => { throw new Error("Synthetic export failure"); });
        await user.click(screen.getByRole("button", { name: "CSV" }));
        expect(screen.getByRole("alert")).toHaveTextContent("Export could not be prepared");
        await user.click(screen.getByRole("button", { name: "Dismiss error" }));
        expect(screen.queryByRole("alert")).toBeNull();
    });
});