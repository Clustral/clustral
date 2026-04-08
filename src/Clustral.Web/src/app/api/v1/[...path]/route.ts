import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * Runtime proxy for ControlPlane REST API.
 * Forwards /api/v1/* to CONTROLPLANE_URL/api/v1/* at request time.
 */
async function handler(
  req: NextRequest,
  { params }: { params: { path: string[] } },
) {
  const controlPlaneUrl =
    process.env.CONTROLPLANE_URL || "http://localhost:5000";
  const path = (await params).path.join("/");
  const target = `${controlPlaneUrl}/api/v1/${path}${req.nextUrl.search}`;

  const headers = new Headers();
  req.headers.forEach((value, key) => {
    if (!["host", "connection", "transfer-encoding"].includes(key.toLowerCase())) {
      headers.set(key, value);
    }
  });

  const res = await fetch(target, {
    method: req.method,
    headers,
    body: req.method !== "GET" && req.method !== "HEAD" ? req.body : undefined,
    // @ts-expect-error -- Node fetch supports duplex for streaming
    duplex: "half",
  });

  const responseHeaders = new Headers();
  const skipResponseHeaders = new Set([
    "transfer-encoding", "connection", "content-encoding", "content-length",
  ]);
  res.headers.forEach((value, key) => {
    if (!skipResponseHeaders.has(key.toLowerCase())) {
      responseHeaders.set(key, value);
    }
  });

  return new NextResponse(res.body, {
    status: res.status,
    headers: responseHeaders,
  });
}

export const GET = handler;
export const POST = handler;
export const PUT = handler;
export const PATCH = handler;
export const DELETE = handler;
