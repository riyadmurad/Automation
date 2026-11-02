import { describe, expect, it } from "vitest";
import type { AuthConfiguration } from "./config";
import { resolveTrustedIdentity } from "./msal";
import type { AuthTransaction } from "./postgres-store";

const trustedTenant = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const objectId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const configuration = {
  trustedTenants: new Set([trustedTenant]),
} as unknown as AuthConfiguration;
const transaction: AuthTransaction = { nonce: "synthetic-nonce", codeVerifier: "synthetic-verifier", returnPath: "/" };
const account = { tenantId: trustedTenant, localAccountId: objectId };
const claims = { nonce: transaction.nonce, tid: trustedTenant, oid: objectId };

describe("MSAL callback identity binding", () => {
  it("accepts a trusted identity only when tenant, immutable object ID, and nonce agree", () => {
    expect(resolveTrustedIdentity(claims, account, transaction, configuration)).toEqual({
      tenantId: trustedTenant,
      objectId,
    });
    expect(() => resolveTrustedIdentity({ ...claims, nonce: "mismatch" }, account, transaction, configuration)).toThrow();
    expect(() => resolveTrustedIdentity({ ...claims, tid: "cccccccc-cccc-4ccc-8ccc-cccccccccccc" }, account, transaction, configuration)).toThrow();
    expect(() => resolveTrustedIdentity(claims, { ...account, localAccountId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd" }, transaction, configuration)).toThrow();
  });
});