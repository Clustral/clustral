import { NextRequest, NextResponse } from "next/server";

const CONTROLPLANE_URL =
  process.env.CONTROLPLANE_URL || "http://localhost:5000";

/**
 * Streaming proxy for kubectl tunnel traffic.
 * Forwards /proxy/{clusterId}/{k8s-path} to the ControlPlane.
 */
async function proxyHandler(
  req: NextRequest,
  { params }: { params: { path: string[] } },
) {
  const path = (await params).path.join("/");
  const target = `${CONTROLPLANE_URL}/proxy/${path}${req.nextUrl.search}`;

  const headers = new Headers();
  req.headers.forEach((value, key) => {
    if (!["host", "connection"].includes(key.toLowerCase())) {
      headers.set(key, value);
    }
  });

  const res = await fetch(target, {
    method: req.method,
    headers,
    body: req.body,
    // @ts-expect-error -- Node fetch supports duplex for streaming
    duplex: "half",
  });

  return new NextResponse(res.body, {
    status: res.status,
    headers: res.headers,
  });
}

export const GET = proxyHandler;
export const POST = proxyHandler;
export const PUT = proxyHandler;
export const PATCH = proxyHandler;
export const DELETE = proxyHandler;
