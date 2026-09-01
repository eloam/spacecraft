import { test, expect, signIn } from "./fixtures";
import { createOrganization, joinByInvitation } from "./workspace";

/**
 * What a role cannot do, seen from the browser.
 *
 * The API's refusals have tests of their own; what those cannot show is what the person in
 * front of the screen is told. A page that refuses silently, or that renders as though the
 * answer were "nothing here", is a different failure from a 403.
 */
test("a member is refused the credentials page, and told why", async ({
  page,
  api,
  account,
  browser,
}) => {
  const organization = await createOrganization(api, account);

  // Managing registry credentials takes Admin: they publish under the organization's name,
  // and a token here is a key to somebody else's registry.
  const member = await joinByInvitation(api, account, organization.id);

  const context = await browser.newContext();
  const theirs = await context.newPage();
  await signIn(theirs, member);

  await theirs.goto(`/orgs/${organization.slug}/credentials`);

  // Named, not counted: the page also carries an informational alert about npm tokens, and
  // "an alert is visible" would have passed on that one alone.
  const refusal = theirs.getByRole("alert").filter({ hasText: "Forbidden" });
  await expect(refusal).toContainText(/requires the Admin role/i);

  // And the owner, on the same page, is not refused.
  await signIn(page, account);
  await page.goto(`/orgs/${organization.slug}/credentials`);
  await expect(page.getByRole("alert").filter({ hasText: "Forbidden" })).toHaveCount(0);

  await context.close();
});
