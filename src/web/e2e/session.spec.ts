import { test, expect, register, PASSWORD_FOR_TESTS } from "./fixtures";
import { createOrganization } from "./workspace";

/**
 * Arriving somewhere while signed out, and leaving on purpose.
 */
test.describe("signed out", () => {
  // No session in this browser: the invitation flow put this exact case in front of people —
  // a link from a mailbox, opened by somebody who has to sign in first — and it used to land
  // them on the dashboard with the link forgotten.
  test.use({ storageState: { cookies: [], origins: [] } });

  test("a link opened signed out comes back to itself after signing in", async ({ page, api }) => {
    const account = await register(api);
    const organization = await createOrganization(api, account);

    const destination = `/orgs/${organization.slug}/members`;
    await page.goto(destination);

    await expect(page).toHaveURL(new RegExp(`/login\\?next=${encodeURIComponent(destination)}`));

    await page.getByLabel("Email").fill(account.email);
    await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(destination);
  });
});

test("signing out ends the session, not just the page you were on", async ({ owner }) => {
  await owner.goto("/");
  await expect(owner.getByRole("heading", { name: "Organizations" })).toBeVisible();

  await owner.getByRole("button", { name: "Account menu" }).click();
  await owner.getByRole("menuitem", { name: "Sign out" }).click();

  await expect(owner).toHaveURL(/\/login/);

  // The tokens are gone, not merely navigated away from — asserted on the store rather than
  // on the back button, which restores a page from memory and proves nothing either way.
  // The store's clear() persists nulls under the same key, so the entry is read, not missed.
  const session = await owner.evaluate(() => window.localStorage.getItem("spacecraft.session"));
  expect(session, "the session entry is still written, with nulls").not.toBeNull();
  expect(JSON.parse(session!).state.accessToken).toBeNull();

  // A fresh arrival with that store: the session was seeded once, before the first page, and
  // nothing puts it back, so this redirect is RequireAuth finding no token.
  await owner.goto("/");
  await expect(owner).toHaveURL(/\/login/);
});
