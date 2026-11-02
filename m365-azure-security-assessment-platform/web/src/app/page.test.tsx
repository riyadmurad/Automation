import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Home from "./page";

describe("server page preview boundary", () => {
    it.each([undefined, "false"])("renders the secure view when opt-in is %s", async (flag) => {
        vi.stubEnv("NODE_ENV", "development");
        vi.stubEnv("ENABLE_SYNTHETIC_PREVIEW", flag);
        render(await Home());
        expect(screen.getByRole("heading", { name: "Workspace unavailable" })).toBeVisible();
        expect(screen.queryByText("Synthetic Customer A")).toBeNull();
    });
    it("cannot expose synthetic data in production even when opt-in is true", async () => {
        vi.stubEnv("NODE_ENV", "production");
        vi.stubEnv("ENABLE_SYNTHETIC_PREVIEW", "true");
        render(await Home());
        expect(screen.getByRole("heading", { name: "Workspace unavailable" })).toBeVisible();
        expect(screen.queryByRole("combobox", { name: "Customer" })).toBeNull();
    });
    it("loads the console only with explicit non-production opt-in", async () => {
        vi.stubEnv("NODE_ENV", "development");
        vi.stubEnv("ENABLE_SYNTHETIC_PREVIEW", "true");
        render(await Home());
        expect(screen.getByRole("heading", { name: "Customer overview" })).toBeVisible();
    });
});