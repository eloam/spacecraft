import {
  test as base,
  expect,
  type APIRequestContext,
  type BrowserContext,
  type BrowserContextOptions,
  type Page,
} from "@playwright/test";
import type { AuthResponse, User } from "../src/api/types";
import { API_URL, WEB_URL } from "./config";

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
  user: User;
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

  await expect(response, `registering ${email}`).toBeOK();

  const body: AuthResponse = await response.json();
  return { email, accessToken: body.accessToken, refreshToken: body.refreshToken, user: body.user };
}

type StorageState = Exclude<BrowserContextOptions["storageState"], string | undefined>;

/**
 * A browser's storage as the application leaves it once someone has signed in.
 *
 * Seeded into a context instead of making a page fill the sign-in form: the form has a test
 * of its own, and every other test would only be re-proving it, at the cost of a page load
 * and a round trip each.
 */
export function sessionOf(account: Account): StorageState {
  return {
    cookies: [],
    origins: [
      {
        origin: WEB_URL,
        localStorage: [
          {
            name: "spacecraft.session",
            value: JSON.stringify({
              state: {
                accessToken: account.accessToken,
                refreshToken: account.refreshToken,
                user: account.user,
              },
              version: 0,
            }),
          },
        ],
      },
    ],
  };
}

export const PASSWORD_FOR_TESTS = PASSWORD;

export const test = base.extend<{
  /** Speaks to the API directly, for everything a test needs to exist but is not testing. */
  api: APIRequestContext;
  account: Account;
  /** A page already signed in as {@link account}. */
  owner: Page;
  /**
   * Opens a page signed in as the given account, in a browser context of its own.
   *
   * A context seeded with storage state rather than an init script: the script re-ran on every
   * navigation and put the original tokens back, so a test could observe neither a sign-out
   * nor a refresh rotation. A factory rather than a `storageState` override: the person it
   * signs in is made in the test body, after the fixtures have been built.
   */
  pageAs: (account: Account) => Promise<Page>;
}>({
  api: async ({ playwright }, use) => {
    const context = await playwright.request.newContext({ baseURL: API_URL });
    await use(context);
    await context.dispose();
  },

  account: async ({ api }, use) => {
    await use(await register(api));
  },

  // Signed in through Playwright's own option rather than in a context opened by hand, so
  // `owner` stays the built-in page and a failure keeps its video: the framework records one
  // only for the context it opens itself. Every built-in page arrives signed in as the account;
  // a test that wants a signed-out browser resets it with
  // `test.use({ storageState: { cookies: [], origins: [] } })`.
  storageState: async ({ account }, use) => {
    await use(sessionOf(account));
  },

  owner: async ({ page }, use) => {
    await use(page);
  },

  pageAs: async ({ browser }, use) => {
    const contexts: BrowserContext[] = [];

    await use(async (account) => {
      const context = await browser.newContext({ storageState: sessionOf(account) });
      contexts.push(context);
      return context.newPage();
    });

    await Promise.all(contexts.map((context) => context.close()));
  },
});

export { expect } from "@playwright/test";
