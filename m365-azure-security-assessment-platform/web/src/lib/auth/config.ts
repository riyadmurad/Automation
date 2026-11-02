export type AuthConfiguration = {
    appOrigin: URL;
    authority: string;
    clientId: string;
    clientSecret: string;
    trustedTenants: ReadonlySet<string>;
    apiOrigin: URL;
    apiScope: string;
    apiAudience: string;
    databaseUrl: string;
    keyVaultUrl: URL;
    keyName: string;
    currentKeyVersion: number;
    keyVersions: ReadonlyMap<number, string>;
    sessionTtlSeconds: number;
    redirectUri: string;
};

function required(environment: NodeJS.ProcessEnv, key: string): string {
    const value = environment[key]?.trim();
    if (!value) throw new Error(`Missing required authentication configuration: ${key}`);
    return value;
}

function trustedTenantIds(raw: string): Set<string> {
    const tenants = raw.split(",").map((tenant) => tenant.trim().toLowerCase());
    if (tenants.length === 0 || tenants.some((tenant) => !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(tenant))) {
        throw new Error("Trusted platform tenants must be a non-empty comma-separated list of tenant GUIDs.");
    }
    return new Set(tenants);
}

function keyVersionMap(raw: string, keyVaultUrl: URL, keyName: string): Map<number, string> {
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
        throw new Error("Key Vault key versions must be a JSON object mapping version numbers to exact versioned key IDs.");
    }

    const versions = new Map<number, string>();
    for (const [versionText, keyId] of Object.entries(parsed)) {
        const version = Number(versionText);
        if (!Number.isSafeInteger(version) || version < 1 || typeof keyId !== "string") {
            throw new Error("Key Vault key versions are invalid.");
        }
        const parsedKeyId = new URL(keyId);
        const expectedPath = `/keys/${encodeURIComponent(keyName)}/`;
        const versionPath = parsedKeyId.pathname.slice(expectedPath.length);
        if (parsedKeyId.protocol !== "https:" || parsedKeyId.origin !== keyVaultUrl.origin ||
            !parsedKeyId.pathname.startsWith(expectedPath) || !versionPath || versionPath.includes("/") ||
            parsedKeyId.search || parsedKeyId.hash) {
            throw new Error("Key Vault key IDs must be versioned keys in the configured vault and key name.");
        }
        versions.set(version, keyId);
    }
    return versions;
}

export function loadAuthConfiguration(environment: NodeJS.ProcessEnv = process.env): AuthConfiguration {
    const production = environment.NODE_ENV === "production";
    const appOrigin = new URL(required(environment, "PLATFORM_WEB_ORIGIN"));
    const apiOrigin = new URL(required(environment, "PLATFORM_API_ORIGIN"));
    if (appOrigin.username || appOrigin.password || appOrigin.search || appOrigin.hash ||
        apiOrigin.username || apiOrigin.password || apiOrigin.search || apiOrigin.hash ||
        appOrigin.pathname !== "/" || apiOrigin.pathname !== "/" ||
        (production && (appOrigin.protocol !== "https:" || apiOrigin.protocol !== "https:")) ||
        (!production && appOrigin.protocol !== "https:" && appOrigin.hostname !== "localhost" && appOrigin.hostname !== "127.0.0.1")) {
        throw new Error("Authentication origins must be canonical HTTPS origins (HTTP is limited to loopback development).");
    }

    const keyVaultUrl = new URL(required(environment, "PLATFORM_KEY_VAULT_URL"));
    if (keyVaultUrl.protocol !== "https:") throw new Error("The Key Vault URL must use HTTPS.");
    const keyName = required(environment, "PLATFORM_SESSION_KEY_NAME");
    const currentKeyVersion = Number(required(environment, "PLATFORM_SESSION_KEY_VERSION"));
    if (!Number.isSafeInteger(currentKeyVersion) || currentKeyVersion < 1) {
        throw new Error("The active session key version must be a positive integer.");
    }
    const keyVersions = keyVersionMap(required(environment, "PLATFORM_SESSION_KEY_VERSIONS"), keyVaultUrl, keyName);
    if (!keyVersions.has(currentKeyVersion)) throw new Error("The active session key version is unavailable.");

    const ttl = Number(environment.PLATFORM_SESSION_TTL_SECONDS ?? "28800");
    if (!Number.isSafeInteger(ttl) || ttl < 300 || ttl > 43200) throw new Error("Session lifetime must be between five minutes and twelve hours.");

    const apiScope = required(environment, "PLATFORM_API_SCOPE");
    const clientId = required(environment, "PLATFORM_ENTRA_CLIENT_ID");
    return {
        appOrigin,
        authority: "https://login.microsoftonline.com/organizations",
        clientId,
        clientSecret: required(environment, "PLATFORM_ENTRA_CLIENT_SECRET"),
        trustedTenants: trustedTenantIds(required(environment, "PLATFORM_TRUSTED_TENANTS")),
        apiOrigin,
        apiScope,
        apiAudience: apiScope.split("/").slice(0, -1).join("/"),
        databaseUrl: required(environment, "PLATFORM_AUTH_DATABASE_URL"),
        keyVaultUrl,
        keyName,
        currentKeyVersion,
        keyVersions,
        sessionTtlSeconds: ttl,
        redirectUri: new URL("/api/auth/callback", appOrigin).toString(),
    };
}

export function safeReturnPath(value: unknown, appOrigin: URL): string {
    if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//") ||
        value.includes("\\") || [...value].some((character) => character.charCodeAt(0) < 32) || value.includes("#")) return "/";
    try {
        const target = new URL(value, appOrigin);
        if (target.origin !== appOrigin.origin || target.pathname.startsWith("/api/auth/")) return "/";
        return `${target.pathname}${target.search}`;
    } catch {
        return "/";
    }
}