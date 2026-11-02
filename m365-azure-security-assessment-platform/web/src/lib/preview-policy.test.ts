import { describe, expect, it } from "vitest";
import { isSyntheticPreviewEnabled } from "./preview-policy";

describe("server synthetic preview policy", () => {
  it.each([undefined, "false", "TRUE", "1", ""])('rejects flag %s', (flag) => {
    expect(isSyntheticPreviewEnabled({ NODE_ENV: "development", ENABLE_SYNTHETIC_PREVIEW: flag })).toBe(false);
  });
  it("rejects production even with explicit opt-in", () => {
    expect(isSyntheticPreviewEnabled({ NODE_ENV: "production", ENABLE_SYNTHETIC_PREVIEW: "true" })).toBe(false);
  });
  it("allows explicit non-production opt-in", () => {
    expect(isSyntheticPreviewEnabled({ NODE_ENV: "development", ENABLE_SYNTHETIC_PREVIEW: "true" })).toBe(true);
  });
});