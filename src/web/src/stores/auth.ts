import { create } from "zustand";
import { persist } from "zustand/middleware";

/**
 * Session state.
 *
 * Zustand holds only what React Query cannot: the tokens. Everything else in this app is
 * server state and lives in the query cache — duplicating it here is the fastest way to end
 * up with two versions of the truth, which is what the identity was: a copy of the account
 * taken at sign-in, patched by hand after a rename, read by the header while the account
 * query held the current one.
 *
 * Security note: tokens live in localStorage, which is readable by any script that manages
 * to run on this origin. The stronger option is an httpOnly, SameSite=Strict refresh cookie
 * issued by the API, with the access token kept in memory only. That is a deliberate
 * follow-up: it needs CSRF handling on the API side and is worth doing before launch.
 */
interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  setSession: (accessToken: string, refreshToken: string) => void;
  updateTokens: (accessToken: string, refreshToken: string) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      setSession: (accessToken, refreshToken) => set({ accessToken, refreshToken }),
      updateTokens: (accessToken, refreshToken) => set({ accessToken, refreshToken }),
      clear: () => set({ accessToken: null, refreshToken: null }),
    }),
    {
      name: "spacecraft.session",
      // Only the tokens reach storage; the actions never did, but saying so is what the
      // middleware offers for it.
      partialize: (state) => ({ accessToken: state.accessToken, refreshToken: state.refreshToken }),
      // Version 0 persisted the identity beside the tokens. A session written by it keeps
      // its tokens and drops the copy, rather than signing everyone out over a shape change.
      version: 1,
      migrate: (persisted) => {
        const { accessToken = null, refreshToken = null } = persisted as Partial<AuthState>;
        return { accessToken, refreshToken };
      },
    },
  ),
);

