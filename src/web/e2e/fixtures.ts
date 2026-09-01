import { test as base, expect, type APIRequestContext, type Page } from "@playwright/test";
import { API_URL } from "./config";

/**
 * Isolation by data rather than by cleaning up.
 *
 * Every test registers its own account and builds its own organization, so two tests running
 * at once cannot see each other's work. The alternative — emptying tables between tests —
 * forces them to run one at a time and turns a shared fixture into a scheduling problem.
 */
const PASSWORD = "correct-horse-battery-staple";

export type Account = {
  email: string;
  accessToken: string;
  refreshToken: string;
  user: { id: string; email: string; displayName: string | null };
};

export function anEmail(prefix = "e2e"): string {
  return `${prefix}-${crypto.randomUUID()}@example.test`;
}

/** The header the API is addressed with. */
export function as(account: Account) {
  return { headers: { Authorization: `Bearer ${account.accessToken}` } };
}

export async function register(
  api: APIRequestContext,
  email = anEmail(),
  displayName?: string,
): Promise<Account> {
  const response = await api.post("/api/v1/auth/register", {
    data: { email, password: PASSWORD, displayName: displayName ?? email.split("@")[0] },
  });

  expect(response.ok(), `registering ${email}: ${response.status()}`).toBeTruthy();

  const body = await response.json();
  return { email, accessToken: body.accessToken, refreshToken: body.refreshToken, user: body.user };
}

/**
 * Hands the browser a session instead of making it fill the sign-in form.
 *
 * The form has a test of its own; every other test would only be re-proving it, at the cost of
 * a page load and a round trip each. Written before any script runs, so the application starts
 * signed in rather than starting out and correcting itself.
 */
export async function signIn(page: Page, account: Account): Promise<void> {
  const session = JSON.stringify({
    state: {
      accessToken: account.accessToken,
      refreshToken: account.refreshToken,
      user: account.user,
    },
    version: 0,
  });

  await page.addInitScript((value) => {
    window.localStorage.setItem("spacecraft.session", value);
  }, session);
}

export const PASSWORD_FOR_TESTS = PASSWORD;

export const test = base.extend<{
  /** Speaks to the API directly, for everything a test needs to exist but is not testing. */
  api: APIRequestContext;
  account: Account;
  /** A page already signed in as {@link account}. */
  owner: Page;
}>({
  api: async ({ playwright }, use) => {
    const context = await playwright.request.newContext({ baseURL: API_URL });
    await use(context);
    await context.dispose();
  },

  account: async ({ api }, use) => {
    await use(await register(api));
  },

  owner: async ({ page, account }, use) => {
    await signIn(page, account);
    await use(page);
  },
});

export { expect } from "@playwright/test";
