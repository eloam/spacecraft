import { test, expect, anEmail, register, PASSWORD_FOR_TESTS } from "./fixtures";

/**
 * The forms every other test skips.
 *
 * Everything else in this suite is handed a session, because paying for a sign-in per test
 * buys nothing once this file has proved it works.
 */
test("creating an account signs you in and lands you on your organizations", async ({ page }) => {
  const email = anEmail("signup");

  await page.goto("/register");
  await page.getByLabel("Name").fill("New Person");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
  await page.getByRole("button", { name: "Create account" }).click();

  await expect(page).toHaveURL("/");
  await expect(page.getByRole("heading", { name: "Organizations" })).toBeVisible();
});

test("an existing account can sign in again", async ({ page, api }) => {
  const account = await register(api);

  await page.goto("/login");
  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page).toHaveURL("/");
  await expect(page.getByRole("heading", { name: "Organizations" })).toBeVisible();
});

test("a wrong password is refused, and says so", async ({ page, api }) => {
  const account = await register(api);

  await page.goto("/login");
  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill("not-the-password");
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});
