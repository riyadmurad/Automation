import { NextRequest } from "next/server";
import { loadAuthConfiguration, safeReturnPath } from "@/lib/auth/config";
import { completeAuthorization, SESSION_COOKIE, TRANSACTION_COOKIE } from "@/lib/auth/msal";
import { getPgAuthStore, sessionCookieValue } from "@/lib/auth/postgres-store";
import { cookieHeader, expiredCookieHeader, noStoreResponse } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: NextRequest): Promise<Response> {
    let configuration;
    try {
        configuration = loadAuthConfiguration();
    } catch {
        return noStoreResponse(JSON.stringify({ error: "authentication_unavailable" }), { status: 503 });
    }

    const state = request.nextUrl.searchParams.get("state");
    const code = request.nextUrl.searchParams.get("code");
    const transactionId = request.cookies.get(TRANSACTION_COOKIE)?.value;
    if (!state || state.length > 128 || !code || code.length > 8192 || !transactionId ||
        request.nextUrl.searchParams.has("error")) {
        return noStoreResponse(JSON.stringify({ error: "invalid_identity_response" }), {
            status: 400,
            headers: { "Set-Cookie": expiredCookieHeader(TRANSACTION_COOKIE) },
        });
    }

    try {
        const store = getPgAuthStore(configuration);
        const transaction = await store.consumeTransaction(transactionId, state);
        if (!transaction) {
            return noStoreResponse(JSON.stringify({ error: "invalid_identity_response" }), {
                status: 400,
                headers: { "Set-Cookie": expiredCookieHeader(TRANSACTION_COOKIE) },
            });
        }

        const session = await completeAuthorization(configuration, store, transaction, state, code);
        const response = Response.redirect(new URL(safeReturnPath(session.returnPath, configuration.appOrigin), configuration.appOrigin), 303);
        response.headers.set("Cache-Control", "no-store");
        response.headers.append("Set-Cookie", cookieHeader(SESSION_COOKIE, sessionCookieValue(session.locator), configuration.sessionTtlSeconds));
        response.headers.append("Set-Cookie", expiredCookieHeader(TRANSACTION_COOKIE));
        return response;
    } catch {
        return noStoreResponse(JSON.stringify({ error: "authentication_failed" }), {
            status: 401,
            headers: { "Set-Cookie": expiredCookieHeader(TRANSACTION_COOKIE) },
        });
    }
}