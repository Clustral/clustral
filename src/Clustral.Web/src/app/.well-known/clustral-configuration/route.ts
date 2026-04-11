import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * CLI discovery endpoint. Returns service URLs and OIDC configuration
 * so the CLI only needs to know the Web UI domain to bootstrap.
 * After discovery, the CLI talks directly to the ControlPlane.
 */
export async function GET() {
  const controlPlaneUrl =
    process.env.CONTROLPLANE_PUBLIC_URL || process.env.CONTROLPLANE_URL;
  return NextResponse.json({
    controlPlaneUrl,
    auditServiceUrl: process.env.AUDIT_SERVICE_PUBLIC_URL || controlPlaneUrl,
    oidcAuthority: process.env.OIDC_ISSUER,
    oidcClientId: "clustral-cli",
    oidcScopes: "openid email profile",
  });
}
