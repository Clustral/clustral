import { UserManager, type UserManagerSettings, WebStorageStateStore } from "oidc-client-ts";

const OIDC_AUTHORITY = import.meta.env.VITE_OIDC_AUTHORITY ?? "http://localhost:8080/realms/clustral";
const OIDC_CLIENT_ID = import.meta.env.VITE_OIDC_CLIENT_ID ?? "clustral-web";
const OIDC_REDIRECT_URI = `${window.location.origin}/callback`;
const OIDC_POST_LOGOUT_URI = `${window.location.origin}/login`;

const settings: UserManagerSettings = {
  authority: OIDC_AUTHORITY,
  client_id: OIDC_CLIENT_ID,
  redirect_uri: OIDC_REDIRECT_URI,
  post_logout_redirect_uri: OIDC_POST_LOGOUT_URI,
  response_type: "code",
  scope: "openid email profile",
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: sessionStorage }),
};

export const userManager = new UserManager(settings);
