import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * Catch-all proxy for the AuditService REST API.
 *
 *   /api/audit/list         → AUDIT_SERVICE_URL/api/v1/audit?...
 *   /api/audit/detail/{uid} → AUDIT_SERVICE_URL/api/v1/audit/{uid}
 */
async function handler(
  req: NextRequest,
  { params }: { params: Promise<{ path: string[] }> },
) {
  const { path } = await params;
  const auditUrl =
    process.env.AUDIT_SERVICE_URL || "http://localhost:5200";

  const subPath = path.join("/");
  let target: string;
  if (subPath === "list") {
    target = `${auditUrl}/api/v1/audit${req.nextUrl.search}`;
  } else if (subPath.startsWith("detail/")) {
    const uid = subPath.slice("detail/".length);
    target = `${auditUrl}/api/v1/audit/${uid}`;
  } else {
    target = `${auditUrl}/api/v1/audit/${subPath}${req.nextUrl.search}`;
  }

  const headers = new Headers();
  req.headers.forEach((value, key) => {
    if (
      !["host", "connection", "transfer-encoding"].includes(key.toLowerCase())
    ) {
      headers.set(key, value);
    }
  });

  const res = await fetch(target, {
    method: req.method,
    headers,
  });

  const responseHeaders = new Headers();
  const skip = new Set([
    "transfer-encoding",
    "connection",
    "content-encoding",
    "content-length",
  ]);
  res.headers.forEach((value, key) => {
    if (!skip.has(key.toLowerCase())) {
      responseHeaders.set(key, value);
    }
  });

  return new NextResponse(res.body, {
    status: res.status,
    headers: responseHeaders,
  });
}

export const GET = handler;
