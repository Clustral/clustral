import { UserManager, type UserManagerSettings, WebStorageStateStore } from "oidc-client-ts";

// Client config — baked at build time, overridable per deployment via env vars.
const OIDC_CLIENT_ID = import.meta.env.VITE_OIDC_CLIENT_ID ?? "clustral-web";
const OIDC_SCOPES = import.meta.env.VITE_OIDC_SCOPES ?? "openid email profile";

let _userManager: UserManager | null = null;
let _initPromise: Promise<UserManager> | null = null;

/**
 * Resolves the OIDC authority URL.
 *
 * In production/on-prem: Keycloak is proxied through nginx at /auth/* on the
 * same origin, avoiding CORS and mixed-content issues. The authority URL
 * becomes e.g. https://192.168.88.x:3000/auth/realms/clustral.
 *
 * We fetch the realm name from the ControlPlane config and build a same-origin
 * authority URL. Falls back to direct Keycloak for localhost dev.
 */
async function resolveAuthority(): Promise<string> {
  // Try fetching the ControlPlane config to get the realm name.
  for (const path of ["/api/v1/config", "/.well-known/clustral-configuration"]) {
    try {
      const res = await fetch(path);
      if (!res.ok) continue;
      const config = await res.json();
      if (config.oidcAuthority) {
        // Extract the realm path from the authority URL.
        // e.g. "http://keycloak:8080/realms/clustral" → "/realms/clustral"
        const url = new URL(config.oidcAuthority);
        const realmPath = url.pathname; // "/realms/clustral"

        // Use same-origin proxied path: /auth/realms/clustral
        return `${window.location.origin}/auth${realmPath}`;
      }
    } catch {
      // Try next.
    }
  }

  // Fallback for local dev (direct Keycloak access via localhost).
  return "http://localhost:8080/realms/clustral";
}

function createUserManager(authority: string): UserManager {
  const settings: UserManagerSettings = {
    authority,
    client_id: OIDC_CLIENT_ID,
    redirect_uri: `${window.location.origin}/callback`,
    post_logout_redirect_uri: `${window.location.origin}/login`,
    response_type: "code",
    scope: OIDC_SCOPES,
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: sessionStorage }),
  };
  return new UserManager(settings);
}

/**
 * Returns the singleton UserManager, initializing it on first call
 * by resolving the OIDC authority.
 */
export async function getUserManager(): Promise<UserManager> {
  if (_userManager) return _userManager;

  if (!_initPromise) {
    _initPromise = resolveAuthority().then((authority) => {
      _userManager = createUserManager(authority);
      return _userManager;
    });
  }

  return _initPromise;
}
