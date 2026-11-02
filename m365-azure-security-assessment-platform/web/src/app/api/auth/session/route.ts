import { NextRequest } from "next/server";
import { loadAuthConfiguration } from "@/lib/auth/config";
import { SESSION_COOKIE } from "@/lib/auth/msal";
import { getPgAuthStore, parseSessionId } from "@/lib/auth/postgres-store";
import { noStoreResponse } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: NextRequest): Promise<Response> {
    try {
        const configuration = loadAuthConfiguration();
        const sessionId = parseSessionId(request.cookies.get(SESSION_COOKIE)?.value);
        const store = getPgAuthStore(configuration);
        const locator = sessionId ? await store.resolveSession(sessionId) : null;
        const session = locator ? await store.loadSession(locator) : null;
        return noStoreResponse(JSON.stringify(session
            ? { authenticated: true, expiresAtUtc: session.expiresAtUtc }
            : { authenticated: false }), { status: session ? 200 : 401 });
    } catch {
        return noStoreResponse(JSON.stringify({ error: "authentication_unavailable" }), { status: 503 });
    }
}