import { test, expect, register, PASSWORD_FOR_TESTS } from "./fixtures";
import { createOrganization } from "./workspace";

/**
 * Arriving somewhere while signed out, and leaving on purpose.
 */
test("a link opened signed out comes back to itself after signing in", async ({ page, api }) => {
  const account = await register(api);
  const organization = await createOrganization(api, account);

  // No session in this browser: the invitation flow put this exact case in front of people —
  // a link from a mailbox, opened by somebody who has to sign in first — and it used to land
  // them on the dashboard with the link forgotten.
  const destination = `/orgs/${organization.slug}/members`;
  await page.goto(destination);

  await expect(page).toHaveURL(new RegExp(`/login\\?next=${encodeURIComponent(destination)}`));

  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page).toHaveURL(destination);
});

test("signing out ends the session, not just the page you were on", async ({ owner }) => {
  await owner.goto("/");
  await expect(owner.getByRole("heading", { name: "Organizations" })).toBeVisible();

  await owner.getByRole("button", { name: "Account menu" }).click();
  await owner.getByRole("menuitem", { name: "Sign out" }).click();

  await expect(owner).toHaveURL(/\/login/);

  // The tokens are gone, not merely navigated away from — asserted on the store rather than
  // on the back button, which restores a page from memory and proves nothing either way.
  const session = await owner.evaluate(() => window.localStorage.getItem("spacecraft.session"));
  expect(session === null || !session.includes("accessToken\":\"ey")).toBeTruthy();

  await owner.goto("/");
  await expect(owner).toHaveURL(/\/login/);
});
