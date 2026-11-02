import {
    ConfidentialClientApplication,
    CryptoProvider,
    type ICachePlugin,
    type TokenCacheContext,
} from "@azure/msal-node";
import { randomUUID } from "node:crypto";
import type { AuthConfiguration } from "./config";
import { PgAuthStore, type AuthTransaction, type SessionLocator } from "./postgres-store";

export const SESSION_COOKIE = "__Host-m365-session";
export const TRANSACTION_COOKIE = "__Host-m365-auth-transaction";

export function createConfidentialClient(
    configuration: AuthConfiguration,
    store: PgAuthStore,
    locator?: SessionLocator,
): ConfidentialClientApplication {
    let loaded = false;
    const cachePlugin: ICachePlugin = {
        beforeCacheAccess: async (context: TokenCacheContext) => {
            if (!locator) return;
            const session = await store.loadSession(locator);
            if (!session) throw new Error("The operator session is expired, revoked, or unavailable.");
            context.tokenCache.deserialize(session.cache);
            loaded = true;
        },
        afterCacheAccess: async (context: TokenCacheContext) => {
            if (locator && loaded && context.cacheHasChanged) {
                await store.updateSessionCache(locator, context.tokenCache.serialize());
            }
        },
    };

    return new ConfidentialClientApplication({
        auth: {
            clientId: configuration.clientId,
            clientSecret: configuration.clientSecret,
            authority: configuration.authority,
        },
        cache: { cachePlugin },
        system: { loggerOptions: { piiLoggingEnabled: false } },
    });
}

export async function beginAuthorization(
    configuration: AuthConfiguration,
    store: PgAuthStore,
    returnPath: string,
): Promise<{ transactionId: string; authorizationUrl: string }> {
    const crypto = new CryptoProvider();
    const { verifier, challenge } = await crypto.generatePkceCodes();
    const state = randomUUID();
    const nonce = crypto.createNewGuid();
    const transaction: AuthTransaction = { nonce, codeVerifier: verifier, returnPath };
    const transactionId = await store.createTransaction(state, transaction);
    const client = createConfidentialClient(configuration, store);
    const authorizationUrl = await client.getAuthCodeUrl({
        scopes: ["openid", "profile", "offline_access", configuration.apiScope],
        redirectUri: configuration.redirectUri,
        responseMode: "query",
        prompt: "select_account",
        state,
        nonce,
        codeChallenge: challenge,
        codeChallengeMethod: "S256",
    });
    return { transactionId, authorizationUrl };
}

export function resolveTrustedIdentity(
    claims: Record<string, unknown> | undefined,
    account: { tenantId: string; localAccountId: string } | null,
    transaction: AuthTransaction,
    configuration: AuthConfiguration,
): { tenantId: string; objectId: string } {
    const tenantId = typeof claims?.tid === "string" ? claims.tid.toLowerCase() : "";
    const objectId = typeof claims?.oid === "string" ? claims.oid.toLowerCase() : "";
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
    if (!claims || claims.nonce !== transaction.nonce || !configuration.trustedTenants.has(tenantId) ||
        !guid.test(objectId) || account?.tenantId.toLowerCase() !== tenantId ||
        account.localAccountId.toLowerCase() !== objectId) {
        throw new Error("The identity response did not match the stored OIDC transaction and trusted tenant configuration.");
    }
    return { tenantId, objectId };
}

export async function completeAuthorization(
    configuration: AuthConfiguration,
    store: PgAuthStore,
    transaction: AuthTransaction,
    state: string,
    authorizationCode: string,
): Promise<{ locator: SessionLocator; expiresAtUtc: string; returnPath: string }> {
    const client = createConfidentialClient(configuration, store);
    const result = await client.acquireTokenByCode({
        code: authorizationCode,
        scopes: ["openid", "profile", "offline_access", configuration.apiScope],
        redirectUri: configuration.redirectUri,
        codeVerifier: transaction.codeVerifier,
        nonce: transaction.nonce,
        state,
    });
    const identity = resolveTrustedIdentity(
        result.idTokenClaims as Record<string, unknown> | undefined,
        result.account,
        transaction,
        configuration,
    );

    const locator: SessionLocator = { sessionId: randomUUID(), ...identity };
    const expiresAtUtc = await store.createSession(locator, client.getTokenCache().serialize());
    return { locator, expiresAtUtc, returnPath: transaction.returnPath };
}

export async function acquireApiToken(
    configuration: AuthConfiguration,
    store: PgAuthStore,
    locator: SessionLocator,
): Promise<string> {
    const client = createConfidentialClient(configuration, store, locator);
    const accounts = await client.getTokenCache().getAllAccounts();
    const account = accounts.find((candidate) => candidate.tenantId.toLowerCase() === locator.tenantId &&
        candidate.localAccountId.toLowerCase() === locator.objectId);
    if (!account) throw new Error("The operator's Entra account is not present in the encrypted session cache.");
    const result = await client.acquireTokenSilent({ account, scopes: [configuration.apiScope] });
    if (!result?.accessToken || result.tenantId.toLowerCase() !== locator.tenantId) {
        throw new Error("The platform API token could not be acquired for this operator tenant.");
    }
    return result.accessToken;
}