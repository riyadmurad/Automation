import { createHash, randomUUID } from "node:crypto";
import { Pool } from "pg";
import type { AuthConfiguration } from "./config";
import { KeyVaultEnvelopeProtector, type EncryptedEnvelope } from "./key-vault-envelope";

export type SessionLocator = { sessionId: string; tenantId: string; objectId: string };
export type AuthTransaction = { nonce: string; codeVerifier: string; returnPath: string };
export type StoredSession = { cache: string; expiresAtUtc: string };

function isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export function parseSessionId(value: string | undefined): string | null {
    return value && isGuid(value) ? value.toLowerCase() : null;
}

export function sessionCookieValue(locator: SessionLocator): string {
    return locator.sessionId;
}

function stateDigest(state: string): string {
    return createHash("sha256").update(state, "utf8").digest("hex");
}

function toEnvelope(row: Record<string, unknown>): EncryptedEnvelope {
    return {
        ciphertext: Buffer.from(row.ciphertext as Buffer),
        nonce: Buffer.from(row.nonce as Buffer),
        authenticationTag: Buffer.from(row.authentication_tag as Buffer),
        wrappedDataKey: Buffer.from(row.wrapped_data_key as Buffer),
        keyId: String(row.key_id),
        keyVersion: Number(row.key_version),
    };
}

export class PgAuthStore {
    private readonly pool: Pool;

    constructor(
        private readonly configuration: AuthConfiguration,
        private readonly protector = new KeyVaultEnvelopeProtector(configuration),
        pool?: Pool,
    ) {
        this.pool = pool ?? new Pool({
            connectionString: configuration.databaseUrl,
            max: 8,
            ssl: process.env.NODE_ENV === "production" ? { rejectUnauthorized: true } : undefined,
            application_name: "m365-security-assessment-web-auth",
        });
    }

    async createTransaction(state: string, transaction: AuthTransaction): Promise<string> {
        const id = randomUUID();
        const context = `oidc-transaction|${id}|${stateDigest(state)}`;
        const plaintext = Buffer.from(JSON.stringify(transaction));
        let envelope: EncryptedEnvelope;
        try {
            envelope = await this.protector.protect(plaintext, context);
        } finally {
            plaintext.fill(0);
        }
        const result = await this.pool.query<{ id: string }>(`INSERT INTO platform.oidc_transactions
      (transaction_id, state_sha256, ciphertext, nonce, authentication_tag,
       wrapped_data_key, key_id, key_version, expires_at_utc)
      VALUES ($1, $2, $3, $4, $5, $6, $7, $8, clock_timestamp() + interval '5 minutes')
      RETURNING transaction_id AS id`, [id, stateDigest(state),
            envelope.ciphertext, envelope.nonce, envelope.authenticationTag, envelope.wrappedDataKey,
            envelope.keyId, envelope.keyVersion]);
        return result.rows[0]!.id;
    }

    async consumeTransaction(id: string, state: string): Promise<AuthTransaction | null> {
        const client = await this.pool.connect();
        try {
            await client.query("BEGIN");
            const result = await client.query(`UPDATE platform.oidc_transactions
        SET consumed_at_utc = clock_timestamp()
        WHERE transaction_id = $1 AND state_sha256 = $2 AND consumed_at_utc IS NULL
          AND expires_at_utc > clock_timestamp()
        RETURNING ciphertext, nonce, authentication_tag, wrapped_data_key, key_id, key_version`, [id, stateDigest(state)]);
            await client.query("COMMIT");
            const row = result.rows[0] as Record<string, unknown> | undefined;
            if (!row) return null;
            const cleartext = await this.protector.unprotect(toEnvelope(row), `oidc-transaction|${id}|${stateDigest(state)}`);
            try {
                return JSON.parse(cleartext.toString("utf8")) as AuthTransaction;
            } finally {
                cleartext.fill(0);
            }
        } catch (error) {
            await client.query("ROLLBACK").catch(() => undefined);
            throw error;
        } finally {
            client.release();
        }
    }

    async createSession(locator: SessionLocator, cache: string): Promise<string> {
        const context = this.sessionContext(locator);
        const plaintext = Buffer.from(cache, "utf8");
        let envelope: EncryptedEnvelope;
        try {
            envelope = await this.protector.protect(plaintext, context);
        } finally {
            plaintext.fill(0);
        }
        const client = await this.pool.connect();
        try {
            await client.query("BEGIN");
            await this.setOperatorContext(client, locator);
            const result = await client.query<{ expires_at_utc: Date }>(`INSERT INTO platform.bff_auth_sessions
        (session_id, operator_tenant_id, operator_object_id, ciphertext, nonce, authentication_tag,
         wrapped_data_key, key_id, key_version, expires_at_utc)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, clock_timestamp() + make_interval(secs => $10))
        RETURNING expires_at_utc`, [locator.sessionId, locator.tenantId, locator.objectId,
            envelope.ciphertext, envelope.nonce, envelope.authenticationTag, envelope.wrappedDataKey,
            envelope.keyId, envelope.keyVersion, this.configuration.sessionTtlSeconds]);
            const expiry = result.rows[0]?.expires_at_utc;
            if (!expiry) throw new Error("The operator session could not be stored.");
            await client.query("COMMIT");
            return expiry.toISOString();
        } catch (error) {
            await client.query("ROLLBACK").catch(() => undefined);
            throw error;
        } finally {
            client.release();
        }
    }

    async loadSession(locator: SessionLocator): Promise<StoredSession | null> {
        const client = await this.pool.connect();
        try {
            await client.query("BEGIN");
            await this.setOperatorContext(client, locator);
            const result = await client.query(`SELECT ciphertext, nonce, authentication_tag, wrapped_data_key,
        key_id, key_version, expires_at_utc FROM platform.bff_auth_sessions
        WHERE session_id = $1 AND operator_tenant_id = $2 AND operator_object_id = $3
          AND revoked_at_utc IS NULL AND expires_at_utc > clock_timestamp()`,
                [locator.sessionId, locator.tenantId, locator.objectId]);
            await client.query("COMMIT");
            const row = result.rows[0] as Record<string, unknown> | undefined;
            if (!row) return null;
            const cleartext = await this.protector.unprotect(toEnvelope(row), this.sessionContext(locator));
            try {
                const session = {
                    cache: cleartext.toString("utf8"),
                    expiresAtUtc: new Date(row.expires_at_utc as Date).toISOString(),
                };
                if (Number(row.key_version) !== this.configuration.currentKeyVersion) {
                    await this.updateSessionCache(locator, session.cache);
                }
                return session;
            } finally {
                cleartext.fill(0);
            }
        } catch (error) {
            await client.query("ROLLBACK").catch(() => undefined);
            throw error;
        } finally {
            client.release();
        }
    }

    async resolveSession(sessionId: string): Promise<SessionLocator | null> {
        if (!isGuid(sessionId)) return null;
        const result = await this.pool.query<{ operator_tenant_id: string; operator_object_id: string }>(
            "SELECT operator_tenant_id, operator_object_id FROM platform.resolve_bff_session($1)",
            [sessionId],
        );
        const row = result.rows[0];
        if (!row) return null;
        return {
            sessionId,
            tenantId: row.operator_tenant_id.toLowerCase(),
            objectId: row.operator_object_id.toLowerCase(),
        };
    }

    async updateSessionCache(locator: SessionLocator, cache: string): Promise<void> {
        const envelope = await this.protector.protect(Buffer.from(cache, "utf8"), this.sessionContext(locator));
        const client = await this.pool.connect();
        try {
            await client.query("BEGIN");
            await this.setOperatorContext(client, locator);
            const result = await client.query(`UPDATE platform.bff_auth_sessions
        SET ciphertext = $4, nonce = $5, authentication_tag = $6, wrapped_data_key = $7,
            key_id = $8, key_version = $9, updated_at_utc = clock_timestamp()
        WHERE session_id = $1 AND operator_tenant_id = $2 AND operator_object_id = $3
          AND revoked_at_utc IS NULL AND expires_at_utc > clock_timestamp()`,
                [locator.sessionId, locator.tenantId, locator.objectId, envelope.ciphertext, envelope.nonce,
                envelope.authenticationTag, envelope.wrappedDataKey, envelope.keyId, envelope.keyVersion]);
            if (result.rowCount !== 1) throw new Error("The operator session is expired or revoked.");
            await client.query("COMMIT");
        } catch (error) {
            await client.query("ROLLBACK").catch(() => undefined);
            throw error;
        } finally {
            client.release();
        }
    }

    async revokeSession(locator: SessionLocator): Promise<void> {
        const client = await this.pool.connect();
        try {
            await client.query("BEGIN");
            await this.setOperatorContext(client, locator);
            await client.query(`UPDATE platform.bff_auth_sessions
        SET revoked_at_utc = COALESCE(revoked_at_utc, clock_timestamp()), updated_at_utc = clock_timestamp()
        WHERE session_id = $1 AND operator_tenant_id = $2 AND operator_object_id = $3`,
                [locator.sessionId, locator.tenantId, locator.objectId]);
            await client.query("COMMIT");
        } catch (error) {
            await client.query("ROLLBACK").catch(() => undefined);
            throw error;
        } finally {
            client.release();
        }
    }

    private async setOperatorContext(client: import("pg").PoolClient, locator: SessionLocator): Promise<void> {
        await client.query(`SELECT set_config('platform.operator_tenant_id', $1, true),
      set_config('platform.operator_object_id', $2, true)`, [locator.tenantId, locator.objectId]);
    }

    private sessionContext(locator: SessionLocator): string {
        return `operator-session|${locator.sessionId}|${locator.tenantId}|${locator.objectId}`;
    }
}

let sharedStore: PgAuthStore | undefined;

export function getPgAuthStore(configuration: AuthConfiguration): PgAuthStore {
    sharedStore ??= new PgAuthStore(configuration);
    return sharedStore;
}