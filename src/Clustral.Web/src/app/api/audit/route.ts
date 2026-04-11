import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * Runtime proxy for AuditService REST API.
 * Forwards /api/audit?... to AUDIT_SERVICE_URL/api/v1/audit?...
 */
async function handler(req: NextRequest) {
  const auditUrl =
    process.env.AUDIT_SERVICE_URL || "http://localhost:5200";
  const target = `${auditUrl}/api/v1/audit${req.nextUrl.search}`;

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
