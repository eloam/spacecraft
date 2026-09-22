import { beforeEach, describe, expect, it } from "vitest";
import type { User } from "@/api/types";
import { useAuthStore } from "./auth";

const user: User = {
  id: "6b1f4c2e-2f6a-4d3b-9c8e-0a1b2c3d4e5f",
  email: "ada@example.test",
  displayName: "Ada",
};

/** What the persist middleware wrote, as the e2e fixtures seed it. */
function persisted() {
  return JSON.parse(localStorage.getItem("spacecraft.session")!).state;
}

beforeEach(() => {
  useAuthStore.getState().clear();
  localStorage.clear();
});

describe("the auth store", () => {
  it("keeps both tokens and the user once a session is set", () => {
    useAuthStore.getState().setSession("access", "refresh", user);

    expect(useAuthStore.getState()).toMatchObject({
      accessToken: "access",
      refreshToken: "refresh",
      user,
    });
  });

  it("replaces the tokens and keeps the user when they are refreshed", () => {
    useAuthStore.getState().setSession("access", "refresh", user);
    useAuthStore.getState().updateTokens("access-2", "refresh-2");

    expect(useAuthStore.getState()).toMatchObject({
      accessToken: "access-2",
      refreshToken: "refresh-2",
      user,
    });
  });

  it("forgets everything when cleared", () => {
    useAuthStore.getState().setSession("access", "refresh", user);
    useAuthStore.getState().clear();

    expect(useAuthStore.getState()).toMatchObject({
      accessToken: null,
      refreshToken: null,
      user: null,
    });
  });

  // The key and the { state, version } shape are what the e2e fixtures write into
  // localStorage to seed a signed-in browser, so they are pinned here.
  it("persists the session under spacecraft.session", () => {
    useAuthStore.getState().setSession("access", "refresh", user);

    expect(persisted()).toEqual({ accessToken: "access", refreshToken: "refresh", user });

    useAuthStore.getState().clear();

    expect(persisted()).toEqual({ accessToken: null, refreshToken: null, user: null });
  });
});
