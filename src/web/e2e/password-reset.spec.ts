import { test, expect, register, PASSWORD_FOR_TESTS } from "./fixtures";
import { linkIn, waitForMessage } from "./mailpit";

/**
 * The one flow whose whole point is that it works for somebody who cannot sign in.
 *
 * It crosses the same boundary the invitation does — a token that exists only in a mailbox —
 * and it ends where nothing else in this suite ends: with a password that has changed.
 */
// Signed out, on purpose: the form is for somebody who cannot sign in, and a session would
// redirect away from it.
test.use({ storageState: { cookies: [], origins: [] } });

test("a forgotten password is reset from the mailbox", async ({ page, api }) => {
  const account = await register(api);
  const newPassword = "a-different-correct-horse-staple";

  await page.goto("/forgot-password");
  await page.getByLabel("Email").fill(account.email);
  await page.getByRole("button", { name: "Send reset link" }).click();

  // The answer says the same thing whether or not the address is known, so there is nothing
  // to assert here beyond "it was accepted" — the mailbox is where the truth is.
  const reset = linkIn(await waitForMessage(api, account.email, "reset"));

  await page.goto(reset.pathname + reset.search);
  await page.getByLabel("New password").fill(newPassword);
  await page.getByLabel("Confirm password").fill(newPassword);
  await page.getByRole("button", { name: "Set new password" }).click();

  // Waited for, not assumed: navigating away while the request was still out cancelled it,
  // and the password that never changed refused the sign-in two lines later.
  await expect(page.getByRole("heading", { name: "Password changed" })).toBeVisible();

  await page.goto("/login");
  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill(newPassword);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page).toHaveURL("/");

  // And the old one no longer opens anything.
  await page.getByRole("button", { name: "Account menu" }).click();
  await page.getByRole("menuitem", { name: "Sign out" }).click();

  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("alert")).toBeVisible();
});
