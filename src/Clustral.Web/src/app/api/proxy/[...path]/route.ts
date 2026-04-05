import { NextRequest, NextResponse } from "next/server";

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
  res.headers.forEach((value, key) => {
    if (!["transfer-encoding", "connection"].includes(key.toLowerCase())) {
      responseHeaders.set(key, value);
    }
  });

  return new NextResponse(res.body, {
    status: res.status,
    headers: responseHeaders,
  });
}

export const GET = proxyHandler;
export const POST = proxyHandler;
export const PUT = proxyHandler;
export const PATCH = proxyHandler;
export const DELETE = proxyHandler;
