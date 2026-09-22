import { beforeEach, describe, expect, it } from "vitest";
import { useAuthStore } from "./auth";

/** What the persist middleware wrote, as the e2e fixtures seed it. */
function persisted() {
  return JSON.parse(localStorage.getItem("spacecraft.session")!);
}

beforeEach(() => {
  useAuthStore.getState().clear();
  localStorage.clear();
});

describe("the auth store", () => {
  it("keeps both tokens once a session is set", () => {
    useAuthStore.getState().setSession("access", "refresh");

    expect(useAuthStore.getState()).toMatchObject({ accessToken: "access", refreshToken: "refresh" });
  });

  it("replaces the tokens when they are refreshed", () => {
    useAuthStore.getState().setSession("access", "refresh");
    useAuthStore.getState().updateTokens("access-2", "refresh-2");

    expect(useAuthStore.getState()).toMatchObject({
      accessToken: "access-2",
      refreshToken: "refresh-2",
    });
  });

  it("forgets both when cleared", () => {
    useAuthStore.getState().setSession("access", "refresh");
    useAuthStore.getState().clear();

    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, refreshToken: null });
  });

  // The key and the { state, version } shape are what the e2e fixtures write into
  // localStorage to seed a signed-in browser, so they are pinned here.
  it("persists the tokens, and nothing else, under spacecraft.session", () => {
    useAuthStore.getState().setSession("access", "refresh");

    expect(persisted()).toEqual({
      state: { accessToken: "access", refreshToken: "refresh" },
      version: 1,
    });

    useAuthStore.getState().clear();

    expect(persisted().state).toEqual({ accessToken: null, refreshToken: null });
  });

  // Version 0 stored a copy of the account beside the tokens. A browser that still holds
  // one must come back signed in, without the copy.
  it("migrates a session written before the identity left the store", async () => {
    localStorage.setItem(
      "spacecraft.session",
      JSON.stringify({
        state: { accessToken: "access", refreshToken: "refresh", user: { id: "1" } },
        version: 0,
      }),
    );

    await useAuthStore.persist.rehydrate();

    expect(useAuthStore.getState()).toMatchObject({ accessToken: "access", refreshToken: "refresh" });
    expect(persisted()).toEqual({
      state: { accessToken: "access", refreshToken: "refresh" },
      version: 1,
    });
  });
});
