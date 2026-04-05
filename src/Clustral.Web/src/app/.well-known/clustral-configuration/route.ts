import { NextResponse } from "next/server";

/**
 * CLI discovery endpoint. Proxies to ControlPlane /api/v1/config at runtime.
 */
export async function GET() {
  const controlPlaneUrl =
    process.env.CONTROLPLANE_URL || "http://localhost:5000";

  try {
    const res = await fetch(`${controlPlaneUrl}/api/v1/config`);
    const data = await res.json();
    return NextResponse.json(data);
  } catch {
    return NextResponse.json(
      { error: "ControlPlane unreachable" },
      { status: 502 },
    );
  }
}
