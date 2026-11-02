import { randomUUID } from "node:crypto";
import { NextRequest } from "next/server";
import { loadAuthConfiguration } from "@/lib/auth/config";
import { acquireApiToken, SESSION_COOKIE } from "@/lib/auth/msal";
import { getPgAuthStore, parseSessionId } from "@/lib/auth/postgres-store";
import { mutationDenied, mutationOriginAllowed, noStoreResponse } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

type RouteContext = { params: Promise<{ path: string[] }> };
const MAX_REQUEST_BYTES = 2_097_152;

async function readBoundedBody(request: Request): Promise<{ body?: ArrayBuffer; tooLarge: boolean }> {
  const declaredLength = Number(request.headers.get("content-length") ?? 0);
  if (declaredLength > MAX_REQUEST_BYTES) return { tooLarge: true };
  if (!request.body) return { body: new ArrayBuffer(0), tooLarge: false };

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalLength = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      totalLength += value.byteLength;
      if (totalLength > MAX_REQUEST_BYTES) {
        await reader.cancel();
        return { tooLarge: true };
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }

  const combined = new Uint8Array(totalLength);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return { body: combined.buffer, tooLarge: false };
}

async function forward(request: NextRequest, context: RouteContext): Promise<Response> {
  let configuration;
  try {
    configuration = loadAuthConfiguration();
  } catch {
    return noStoreResponse(JSON.stringify({ error: "platform_unavailable" }), { status: 503 });
  }

  if (!new Set(["GET", "HEAD", "OPTIONS"]).has(request.method) &&
    !mutationOriginAllowed(request, configuration.appOrigin)) return mutationDenied();

  const sessionId = parseSessionId(request.cookies.get(SESSION_COOKIE)?.value);
  let locator;
  try {
    locator = sessionId ? await getPgAuthStore(configuration).resolveSession(sessionId) : null;
  } catch {
    return noStoreResponse(JSON.stringify({ error: "platform_unavailable" }), { status: 503 });
  }
  if (!locator) return noStoreResponse(JSON.stringify({ error: "unauthorized" }), { status: 401 });

  const { path } = await context.params;
  if (!path.length || path.some((segment) => !/^[A-Za-z0-9._~-]+$/.test(segment) || segment === "." || segment === "..")) {
    return noStoreResponse(JSON.stringify({ error: "not_found" }), { status: 404 });
  }
  const url = new URL(`/platform/${path.map(encodeURIComponent).join("/")}`, configuration.apiOrigin);
  for (const [key, value] of request.nextUrl.searchParams) {
    if (/^(access_token|id_token|refresh_token|client_secret|code)$/i.test(key)) {
      return noStoreResponse(JSON.stringify({ error: "invalid_request" }), { status: 400 });
    }
    url.searchParams.append(key, value);
  }

  let body: ArrayBuffer | undefined;
  if (!new Set(["GET", "HEAD", "OPTIONS"]).has(request.method)) {
    const boundedBody = await readBoundedBody(request);
    if (boundedBody.tooLarge) return noStoreResponse(JSON.stringify({ error: "request_too_large" }), { status: 413 });
    body = boundedBody.body;
  }

  try {
    const accessToken = await acquireApiToken(configuration, getPgAuthStore(configuration), locator);
    const headers = new Headers({
      Authorization: `Bearer ${accessToken}`,
      Accept: request.headers.get("accept") ?? "application/json",
      "X-Request-ID": randomUUID(),
    });
    const contentType = request.headers.get("content-type");
    if (contentType && body) headers.set("Content-Type", contentType);
    const upstream = await fetch(url, {
      method: request.method,
      headers,
      body,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(30_000),
    });
    if (upstream.status >= 300 && upstream.status < 400) {
      return noStoreResponse(JSON.stringify({ error: "upstream_redirect_blocked" }), { status: 502 });
    }
    const responseHeaders = new Headers();
    const responseContentType = upstream.headers.get("content-type");
    const disposition = upstream.headers.get("content-disposition");
    if (responseContentType) responseHeaders.set("Content-Type", responseContentType);
    if (disposition) responseHeaders.set("Content-Disposition", disposition);
    return noStoreResponse(upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch {
    return noStoreResponse(JSON.stringify({ error: "platform_unavailable" }), { status: 503 });
  }
}

export const GET = forward;
export const HEAD = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;