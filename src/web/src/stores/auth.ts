import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { User } from "@/api/types";

/**
 * Session state.
 *
 * Zustand holds only what React Query cannot: the tokens and the identity they belong to.
 * Everything else in this app is server state and lives in the query cache — duplicating
 * it here is the fastest way to end up with two versions of the truth.
 *
 * Security note: tokens live in localStorage, which is readable by any script that manages
 * to run on this origin. The stronger option is an httpOnly, SameSite=Strict refresh cookie
 * issued by the API, with the access token kept in memory only. That is a deliberate
 * follow-up: it needs CSRF handling on the API side and is worth doing before launch.
 */
interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: User | null;
  setSession: (accessToken: string, refreshToken: string, user: User) => void;
  updateTokens: (accessToken: string, refreshToken: string) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      user: null,
      setSession: (accessToken, refreshToken, user) => set({ accessToken, refreshToken, user }),
      updateTokens: (accessToken, refreshToken) => set({ accessToken, refreshToken }),
      clear: () => set({ accessToken: null, refreshToken: null, user: null }),
    }),
    { name: "spacecraft.session" },
  ),
);

export const isAuthenticated = () => useAuthStore.getState().accessToken !== null;
