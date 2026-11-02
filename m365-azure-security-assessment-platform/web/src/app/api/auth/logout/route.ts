import { NextRequest } from "next/server";
import { loadAuthConfiguration } from "@/lib/auth/config";
import { SESSION_COOKIE } from "@/lib/auth/msal";
import { getPgAuthStore, parseSessionId } from "@/lib/auth/postgres-store";
import { expiredCookieHeader, mutationDenied, mutationOriginAllowed, noStoreResponse } from "@/lib/auth/security";

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

  const sessionId = parseSessionId(request.cookies.get(SESSION_COOKIE)?.value);
  if (sessionId) {
    try {
      const store = getPgAuthStore(configuration);
      const locator = await store.resolveSession(sessionId);
      if (locator) await store.revokeSession(locator);
    } catch {
      return noStoreResponse(JSON.stringify({ error: "logout_unavailable" }), { status: 503 });
    }
  }

  const logoutUrl = new URL("/oauth2/v2.0/logout", configuration.authority);
  logoutUrl.searchParams.set("post_logout_redirect_uri", configuration.appOrigin.origin);
  const response = Response.redirect(logoutUrl, 303);
  response.headers.set("Cache-Control", "no-store");
  response.headers.append("Set-Cookie", expiredCookieHeader(SESSION_COOKIE));
  return response;
}