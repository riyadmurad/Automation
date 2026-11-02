import { NextRequest } from "next/server";
import { loadAuthConfiguration, safeReturnPath } from "@/lib/auth/config";
import { beginAuthorization, TRANSACTION_COOKIE } from "@/lib/auth/msal";
import { getPgAuthStore } from "@/lib/auth/postgres-store";
import { cookieHeader, mutationDenied, mutationOriginAllowed, noStoreResponse } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function POST(request: NextRequest): Promise<Response> {
    let configuration;
    try {
        configuration = loadAuthConfiguration();
    } catch {
        return noStoreResponse(JSON.stringify({ error: "authentication_unavailable" }), { status: 503 });
    }
    if (!mutationOriginAllowed(request, configuration.appOrigin)) return mutationDenied();

    let requestedReturnPath: unknown;
    try {
        const body: unknown = await request.json();
        requestedReturnPath = typeof body === "object" && body !== null && "returnTo" in body
            ? body.returnTo
            : undefined;
    } catch {
        return noStoreResponse(JSON.stringify({ error: "invalid_request" }), { status: 400 });
    }

    try {
        const store = getPgAuthStore(configuration);
        const result = await beginAuthorization(configuration, store, safeReturnPath(requestedReturnPath, configuration.appOrigin));
        const response = Response.redirect(result.authorizationUrl, 303);
        response.headers.set("Cache-Control", "no-store");
        response.headers.append("Set-Cookie", cookieHeader(TRANSACTION_COOKIE, result.transactionId, 300));
        return response;
    } catch {
        return noStoreResponse(JSON.stringify({ error: "authentication_unavailable" }), { status: 503 });
    }
}