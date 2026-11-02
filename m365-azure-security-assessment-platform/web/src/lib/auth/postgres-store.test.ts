import { createHash } from "node:crypto";
import { describe, expect, it, vi } from "vitest";
import type { Pool } from "pg";
import type { AuthConfiguration } from "./config";
import { PgAuthStore, parseSessionId, sessionCookieValue, type SessionLocator } from "./postgres-store";

const configuration = {
  appOrigin: new URL("https://security.example.test"),
  authority: "https://login.microsoftonline.com/organizations",
  clientId: "synthetic-client-id",
  clientSecret: "synthetic-test-secret",
  trustedTenants: new Set(["aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"]),
  apiOrigin: new URL("https://api.example.test"),
  apiScope: "api://synthetic-assessment/access_as_user",
  apiAudience: "api://synthetic-assessment",
  databaseUrl: "postgresql://test:test@127.0.0.1/test",
  keyVaultUrl: new URL("https://vault.example.test"),
  keyName: "session",
  currentKeyVersion: 1,
  keyVersions: new Map([[1, "https://vault.example.test/keys/session/v1"]]),
  sessionTtlSeconds: 3600,
  redirectUri: "https://security.example.test/api/auth/callback",
} satisfies AuthConfiguration;

const protectedPayload = (payload: Uint8Array, keyVersion = 1) => ({
  ciphertext: Buffer.from(payload),
  nonce: Buffer.alloc(12, 1),
  authenticationTag: Buffer.alloc(16, 2),
  wrappedDataKey: Buffer.alloc(256, 3),
  keyId: `https://vault.example.test/keys/session/v${keyVersion}`,
  keyVersion,
});

function fakeProtectorFor(keyVersion = 1) {
  return {
    protect: vi.fn(async (payload: Uint8Array) => protectedPayload(payload, keyVersion)),
    unprotect: vi.fn(async (envelope: { ciphertext: Buffer }) => Buffer.from(envelope.ciphertext)),
  };
}

const fakeProtector = fakeProtectorFor();

function fakePool(queryHandler: (sql: string, values?: unknown[]) => Promise<{ rows: Record<string, unknown>[]; rowCount: number }>) {
  const client = {
    query: vi.fn((sql: string, values?: unknown[]) => queryHandler(sql, values)),
    release: vi.fn(),
  };
  return { pool: { connect: vi.fn(async () => client), query: vi.fn() } as unknown as Pool, client };
}

describe("PostgreSQL BFF session partitioning", () => {
  it("sets RLS identity before inserting an encrypted session with database UTC expiry", async () => {
    const locator: SessionLocator = {
      sessionId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      tenantId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      objectId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    };
    const { pool, client } = fakePool(async (sql) => sql.startsWith("INSERT INTO platform.bff_auth_sessions")
      ? { rows: [{ expires_at_utc: new Date("2030-01-01T00:00:00.000Z") }], rowCount: 1 }
      : { rows: [], rowCount: 0 });
    const store = new PgAuthStore(configuration, fakeProtector as never, pool);

    const expiresAt = await store.createSession(locator, "synthetic-encrypted-cache");
    const statements = client.query.mock.calls.map(([sql]) => sql);

    expect(expiresAt).toBe("2030-01-01T00:00:00.000Z");
    expect(statements[0]).toBe("BEGIN");
    expect(statements[1]).toContain("set_config('platform.operator_tenant_id'");
    expect(statements[2]).toContain("INSERT INTO platform.bff_auth_sessions");
    expect(statements[3]).toBe("COMMIT");
  });

  it("consumes an OIDC state transaction once and rejects replay", async () => {
    let updateCount = 0;
    const row = {
      ciphertext: Buffer.from(JSON.stringify({ nonce: "n", codeVerifier: "v", returnPath: "/" })),
      nonce: Buffer.alloc(12, 1),
      authentication_tag: Buffer.alloc(16, 2),
      wrapped_data_key: Buffer.alloc(256, 3),
      key_id: "https://vault.example.test/keys/session/v1",
      key_version: 1,
    };
    const { pool, client } = fakePool(async (sql, values) => {
      if (sql.startsWith("UPDATE platform.oidc_transactions")) {
        if (values?.[1] !== createHash("sha256").update("state-value", "utf8").digest("hex")) {
          return { rows: [], rowCount: 0 };
        }
        updateCount += 1;
        return { rows: updateCount === 1 ? [row] : [], rowCount: updateCount === 1 ? 1 : 0 };
      }
      return { rows: [], rowCount: 0 };
    });
    const store = new PgAuthStore(configuration, fakeProtector as never, pool);

    const mismatch = await store.consumeTransaction("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "wrong-state");
    const first = await store.consumeTransaction("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "state-value");
    const replay = await store.consumeTransaction("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "state-value");

    expect(mismatch).toBeNull();
    expect(first?.codeVerifier).toBe("v");
    expect(replay).toBeNull();
    expect(client.query).toHaveBeenCalledWith(expect.stringContaining("consumed_at_utc IS NULL"), expect.any(Array));
    expect(client.query).toHaveBeenCalledWith(expect.stringContaining("expires_at_utc > clock_timestamp()"), expect.any(Array));
  });

  it("only loads an encrypted cache for the immutable user and tenant in its locator", async () => {
    const storedLocator: SessionLocator = {
      sessionId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      tenantId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      objectId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    };
    const row = {
      ciphertext: Buffer.from("synthetic-msal-cache"),
      nonce: Buffer.alloc(12, 1),
      authentication_tag: Buffer.alloc(16, 2),
      wrapped_data_key: Buffer.alloc(256, 3),
      key_id: "https://vault.example.test/keys/session/v1",
      key_version: 1,
      expires_at_utc: new Date(Date.now() + 60_000),
    };
    const { pool, client } = fakePool(async (sql, values) => {
      if (sql.startsWith("SELECT ciphertext")) {
        return values?.[0] === storedLocator.sessionId && values[1] === storedLocator.tenantId && values[2] === storedLocator.objectId
          ? { rows: [row], rowCount: 1 }
          : { rows: [], rowCount: 0 };
      }
      return { rows: [], rowCount: 0 };
    });
    const store = new PgAuthStore(configuration, fakeProtector as never, pool);

    expect(await store.loadSession(storedLocator)).toMatchObject({ cache: "synthetic-msal-cache" });
    expect(await store.loadSession({ ...storedLocator, objectId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd" })).toBeNull();
    expect(client.query).toHaveBeenCalledWith(expect.stringContaining("set_config('platform.operator_tenant_id'"), [storedLocator.tenantId, storedLocator.objectId]);
  });

  it("re-encrypts an active cache with the current key version after successful decryption", async () => {
    const locator: SessionLocator = {
      sessionId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      tenantId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      objectId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    };
    const oldEnvelopeRow = {
      ciphertext: Buffer.from("synthetic-msal-cache"),
      nonce: Buffer.alloc(12, 1),
      authentication_tag: Buffer.alloc(16, 2),
      wrapped_data_key: Buffer.alloc(256, 3),
      key_id: "https://vault.example.test/keys/session/v1",
      key_version: 1,
      expires_at_utc: new Date(Date.now() + 60_000),
    };
    const rotatedConfiguration = {
      ...configuration,
      currentKeyVersion: 2,
      keyVersions: new Map([
        [1, "https://vault.example.test/keys/session/v1"],
        [2, "https://vault.example.test/keys/session/v2"],
      ]),
    } satisfies AuthConfiguration;
    const { pool, client } = fakePool(async (sql) => {
      if (sql.startsWith("SELECT ciphertext")) return { rows: [oldEnvelopeRow], rowCount: 1 };
      if (sql.startsWith("UPDATE platform.bff_auth_sessions")) return { rows: [], rowCount: 1 };
      return { rows: [], rowCount: 0 };
    });
    const store = new PgAuthStore(rotatedConfiguration, fakeProtectorFor(2) as never, pool);

    expect(await store.loadSession(locator)).toMatchObject({ cache: "synthetic-msal-cache" });
    const rotatedWrite = client.query.mock.calls.find(([sql]) => sql.startsWith("UPDATE platform.bff_auth_sessions"));
    expect(rotatedWrite?.[1]?.[8]).toBe(2);
  });

  it("uses an opaque server-resolved session identifier in the browser cookie", () => {
    const locator: SessionLocator = {
      sessionId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      tenantId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      objectId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    };
    expect(sessionCookieValue(locator)).toBe(locator.sessionId);
    expect(parseSessionId("bad-cookie")).toBeNull();
    expect(parseSessionId(locator.sessionId)).toBe(locator.sessionId);
  });

  it("resolves the immutable user and tenant from the active server-side session", async () => {
    const locator: SessionLocator = {
      sessionId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      tenantId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      objectId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    };
    const pool = {
      query: vi.fn(async () => ({
        rows: [{ operator_tenant_id: locator.tenantId, operator_object_id: locator.objectId }],
        rowCount: 1,
      })),
    } as unknown as Pool;
    const store = new PgAuthStore(configuration, fakeProtector as never, pool);

    expect(await store.resolveSession(locator.sessionId)).toEqual(locator);
    expect(await store.resolveSession("not-a-session-id")).toBeNull();
    expect(pool.query).toHaveBeenCalledWith(
      "SELECT operator_tenant_id, operator_object_id FROM platform.resolve_bff_session($1)",
      [locator.sessionId],
    );
  });
});