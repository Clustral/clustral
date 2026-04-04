import { create } from "zustand";
import type { UserInfo } from "@/types/api";
import { getUserManager } from "@/lib/oidc";

interface AuthState {
  token: string | null;
  user: UserInfo | null;
  loading: boolean;
  setAuth: (token: string) => void;
  clearAuth: () => void;
  login: () => Promise<void>;
  logout: () => Promise<void>;
  handleCallback: () => Promise<void>;
  restoreSession: () => Promise<void>;
}

function decodeUserInfo(token: string): UserInfo | null {
  try {
    const payload = token.split(".")[1];
    if (!payload) return null;
    const json = JSON.parse(atob(payload));
    return {
      sub: json.sub ?? "",
      email: json.email ?? json.preferred_username ?? "",
      name: json.name ?? json.given_name ?? "",
    };
  } catch {
    return null;
  }
}

export const useAuthStore = create<AuthState>((set) => ({
  token: null,
  user: null,
  loading: true,

  setAuth: (token: string) => {
    set({ token, user: decodeUserInfo(token), loading: false });
  },

  clearAuth: () => {
    set({ token: null, user: null, loading: false });
  },

  login: async () => {
    const um = await getUserManager();
    await um.signinRedirect();
  },

  logout: async () => {
    set({ token: null, user: null, loading: false });
    const um = await getUserManager();
    await um.signoutRedirect();
  },

  handleCallback: async () => {
    const um = await getUserManager();
    const user = await um.signinRedirectCallback();
    const token = user.access_token;
    set({ token, user: decodeUserInfo(token), loading: false });
  },

  restoreSession: async () => {
    try {
      const um = await getUserManager();
      const user = await um.getUser();
      if (user && !user.expired) {
        set({ token: user.access_token, user: decodeUserInfo(user.access_token), loading: false });
      } else {
        set({ loading: false });
      }
    } catch {
      set({ loading: false });
    }
  },
}));

// Set up event listeners once the UserManager is initialized.
getUserManager().then((um) => {
  um.events.addAccessTokenExpired(() => {
    useAuthStore.getState().clearAuth();
  });
  um.events.addUserLoaded((user) => {
    useAuthStore.getState().setAuth(user.access_token);
  });
});
