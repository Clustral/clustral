import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * Streaming proxy for kubectl tunnel traffic.
 * Forwards /api/proxy/{clusterId}/{k8s-path} to the ControlPlane.
 */
async function proxyHandler(
  req: NextRequest,
  { params }: { params: { path: string[] } },
) {
  const controlPlaneUrl =
    process.env.CONTROLPLANE_URL || "http://localhost:5000";
  const path = (await params).path.join("/");
  const target = `${controlPlaneUrl}/api/proxy/${path}${req.nextUrl.search}`;

  // Forward all headers except hop-by-hop ones.
  // Explicitly grab Authorization — kubectl sends it as a Bearer token.
  const outHeaders: Record<string, string> = {};
  const skipHeaders = new Set(["host", "connection", "transfer-encoding"]);

  req.headers.forEach((value, key) => {
    if (!skipHeaders.has(key.toLowerCase())) {
      outHeaders[key] = value;
    }
  });

  const res = await fetch(target, {
    method: req.method,
    headers: outHeaders,
    body: req.method !== "GET" && req.method !== "HEAD" ? await req.arrayBuffer() : undefined,
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

  const body = await res.arrayBuffer();
  return new NextResponse(body, {
    status: res.status,
    headers: responseHeaders,
  });
}

export const GET = proxyHandler;
export const POST = proxyHandler;
export const PUT = proxyHandler;
export const PATCH = proxyHandler;
export const DELETE = proxyHandler;
