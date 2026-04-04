import { UserManager, type UserManagerSettings, WebStorageStateStore } from "oidc-client-ts";

// Client config — baked at build time, overridable per deployment via env vars.
const OIDC_CLIENT_ID = import.meta.env.VITE_OIDC_CLIENT_ID ?? "clustral-web";
const OIDC_SCOPES = import.meta.env.VITE_OIDC_SCOPES ?? "openid email profile";

let _userManager: UserManager | null = null;
let _initPromise: Promise<UserManager> | null = null;

/**
 * Fetches the Keycloak authority URL from the ControlPlane at runtime,
 * so the published Docker image works with any Keycloak deployment.
 */
async function fetchAuthority(): Promise<string> {
  for (const path of ["/api/v1/config", "/.well-known/clustral-configuration"]) {
    try {
      const res = await fetch(path);
      if (res.ok) {
        const config = await res.json();
        if (config.oidcAuthority) return config.oidcAuthority;
      }
    } catch {
      // Try next.
    }
  }

  // Fallback for local dev without the ControlPlane running.
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
 * by fetching the Keycloak authority from the ControlPlane.
 */
export async function getUserManager(): Promise<UserManager> {
  if (_userManager) return _userManager;

  if (!_initPromise) {
    _initPromise = fetchAuthority().then((authority) => {
      _userManager = createUserManager(authority);
      return _userManager;
    });
  }

  return _initPromise;
}
