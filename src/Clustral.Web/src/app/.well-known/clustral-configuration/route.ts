import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * CLI discovery endpoint. Returns service URLs, OIDC configuration, and
 * ControlPlane version so the CLI only needs to know the Web UI domain
 * to bootstrap. After discovery, the CLI talks directly to the ControlPlane.
 */
export async function GET() {
  const controlPlaneUrl =
    process.env.CONTROLPLANE_PUBLIC_URL || process.env.CONTROLPLANE_URL;
  const internalCpUrl = process.env.CONTROLPLANE_URL;

  // Fetch ControlPlane version (best-effort — don't fail discovery if unreachable).
  let version: string | undefined;
  if (internalCpUrl) {
    try {
      const res = await fetch(`${internalCpUrl}/api/v1/version`, {
        signal: AbortSignal.timeout(3000),
      });
      if (res.ok) {
        const data = await res.json();
        version = data.version;
      }
    } catch {
      // ControlPlane unreachable — omit version field.
    }
  }

  return NextResponse.json({
    version,
    controlPlaneUrl,
    auditServiceUrl: process.env.AUDIT_SERVICE_PUBLIC_URL || controlPlaneUrl,
    oidcAuthority: process.env.OIDC_ISSUER,
    oidcClientId: "clustral-cli",
    oidcScopes: "openid email profile",
  });
}
