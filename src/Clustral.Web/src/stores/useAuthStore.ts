import { create } from "zustand";
import type { UserInfo } from "@/types/api";
import { userManager } from "@/lib/oidc";

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
    await userManager.signinRedirect();
  },

  logout: async () => {
    set({ token: null, user: null, loading: false });
    await userManager.signoutRedirect();
  },

  handleCallback: async () => {
    const user = await userManager.signinRedirectCallback();
    const token = user.access_token;
    set({ token, user: decodeUserInfo(token), loading: false });
  },

  restoreSession: async () => {
    try {
      const user = await userManager.getUser();
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

// Listen for silent-renew token updates.
userManager.events.addAccessTokenExpired(() => {
  useAuthStore.getState().clearAuth();
});

userManager.events.addUserLoaded((user) => {
  useAuthStore.getState().setAuth(user.access_token);
});
