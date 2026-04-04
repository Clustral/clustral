import { UserManager, type UserManagerSettings, WebStorageStateStore } from "oidc-client-ts";

interface ClustralConfig {
  oidcAuthority: string;
  oidcClientId: string;
  oidcScopes: string;
}

let _userManager: UserManager | null = null;
let _initPromise: Promise<UserManager> | null = null;

/**
 * Fetches OIDC settings from the ControlPlane at runtime, so the Web UI
 * doesn't need build-time env vars and works with any deployment.
 */
async function fetchConfig(): Promise<ClustralConfig> {
  // Try the discovery endpoint (same-origin, proxied by nginx).
  for (const path of ["/api/v1/config", "/.well-known/clustral-configuration"]) {
    try {
      const res = await fetch(path);
      if (res.ok) return await res.json();
    } catch {
      // Try next.
    }
  }

  // Fallback for local dev without the ControlPlane running.
  return {
    oidcAuthority: "http://localhost:8080/realms/clustral",
    oidcClientId: "clustral-web",
    oidcScopes: "openid email profile",
  };
}

function createUserManager(config: ClustralConfig): UserManager {
  const settings: UserManagerSettings = {
    authority: config.oidcAuthority,
    client_id: config.oidcClientId,
    redirect_uri: `${window.location.origin}/callback`,
    post_logout_redirect_uri: `${window.location.origin}/login`,
    response_type: "code",
    scope: config.oidcScopes,
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: sessionStorage }),
  };
  return new UserManager(settings);
}

/**
 * Returns the singleton UserManager, initializing it on first call
 * by fetching OIDC config from the ControlPlane.
 */
export async function getUserManager(): Promise<UserManager> {
  if (_userManager) return _userManager;

  if (!_initPromise) {
    _initPromise = fetchConfig().then((config) => {
      _userManager = createUserManager(config);
      return _userManager;
    });
  }

  return _initPromise;
}
