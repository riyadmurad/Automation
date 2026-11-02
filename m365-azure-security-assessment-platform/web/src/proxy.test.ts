import { NextRequest } from "next/server";
import { describe, expect, it, vi } from "vitest";
import { proxy } from "./proxy";

describe("workspace response security", () => {
  it("sets private no-store headers and a per-request production CSP nonce", () => {
    vi.stubEnv("NODE_ENV", "production");
    const request = new NextRequest("http://127.0.0.1/");
    const response = proxy(request);
    expect(response.headers.get("Cache-Control")).toContain("private, no-store");
    const csp = response.headers.get("Content-Security-Policy")!;
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("connect-src 'self'");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).toMatch(/script-src 'self' 'nonce-[^']+'/);
    expect(proxy(request).headers.get("Content-Security-Policy")).not.toBe(csp);
    expect(response.headers.get("Referrer-Policy")).toBe("no-referrer");
  });
});