import { test, expect, register, PASSWORD_FOR_TESTS } from "./fixtures";

/**
 * The account locks, and the page says so.
 *
 * This protection was configured for the whole life of the feature and never armed: nothing
 * counted a failure, so the branch that checks for a lockout was unreachable while a comment
 * two files away leaned on it in writing. It is counted now, and pinned by an integration
 * test — but nobody had ever read what the tenth attempt puts in front of somebody who is, by
 * then, already worried.
 */
const MAX_ATTEMPTS = 10;

test("too many wrong passwords lock the account, and the right one no longer opens it", async ({
  page,
  api,
}) => {
  const account = await register(api);

  // Through the API, because the point is what happens after them, not the typing.
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const refused = await api.post("/api/v1/auth/login", {
      data: { email: account.email, password: "not-the-password" },
    });

    expect(refused.ok()).toBeFalsy();
  }

  await page.goto("/login");
  await page.getByLabel("Email").fill(account.email);

  // The correct one, deliberately. A lockout that only refuses wrong guesses stops nothing:
  // an attacker's last guess is the right one.
  await page.getByLabel("Password").fill(PASSWORD_FOR_TESTS);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("alert")).toContainText(/too many failed attempts/i);
  await expect(page).toHaveURL(/\/login/);
});
