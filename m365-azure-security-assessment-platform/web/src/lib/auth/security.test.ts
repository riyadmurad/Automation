import { describe, expect, it } from "vitest";
import { loadAuthConfiguration, safeReturnPath } from "./config";
import { mutationOriginAllowed } from "./security";

const origin = new URL("https://security.example.test");

describe("auth request security", () => {
  it("accepts only same-origin mutation requests", () => {
    expect(mutationOriginAllowed(new Request(origin, {
      method: "POST",
      headers: { origin: origin.origin, "sec-fetch-site": "same-origin" },
    }), origin)).toBe(true);
    expect(mutationOriginAllowed(new Request(origin, {
      method: "POST",
      headers: { origin: "https://attacker.example", "sec-fetch-site": "cross-site" },
    }), origin)).toBe(false);
    expect(mutationOriginAllowed(new Request(origin, { method: "POST" }), origin)).toBe(false);
  });

  it("keeps post-login navigation on the configured origin", () => {
    expect(safeReturnPath("/customers/overview?filter=active", origin)).toBe("/customers/overview?filter=active");
    expect(safeReturnPath("https://attacker.example/steal", origin)).toBe("/");
    expect(safeReturnPath("//attacker.example/steal", origin)).toBe("/");
    expect(safeReturnPath("/api/auth/logout", origin)).toBe("/");
    expect(safeReturnPath("/\\attacker.example", origin)).toBe("/");
  });

  it("fails closed when the identity and storage configuration is absent", () => {
    expect(() => loadAuthConfiguration({ NODE_ENV: "test" })).toThrow("PLATFORM_WEB_ORIGIN");
  });
});