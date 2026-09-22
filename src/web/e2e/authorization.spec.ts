import { test, expect } from "./fixtures";
import { createOrganization, joinByInvitation } from "./workspace";

/**
 * What a role cannot do, seen from the browser.
 *
 * The API's refusals have tests of their own; what those cannot show is what the person in
 * front of the screen is told. A page that refuses silently, or that renders as though the
 * answer were "nothing here", is a different failure from a 403.
 */
test("a member is refused the credentials page, and told why", async ({
  owner,
  api,
  account,
  pageAs,
}) => {
  const organization = await createOrganization(api, account);

  // Managing registry credentials takes Admin: they publish under the organization's name,
  // and a token here is a key to somebody else's registry.
  const member = await joinByInvitation(api, account, organization.id);

  const theirs = await pageAs(member);
  await theirs.goto(`/orgs/${organization.slug}/credentials`);

  // Named, not counted: the page also carries an informational alert about npm tokens, and
  // "an alert is visible" would have passed on that one alone.
  const refusal = theirs.getByRole("alert").filter({ hasText: "Forbidden" });
  await expect(refusal).toContainText(/requires the Admin role/i);

  // And the owner, on the same page, is not refused. The empty state comes first: it is what
  // the query renders once it has answered, and "no refusal" on a page still showing its
  // skeleton would have passed before the answer arrived.
  await owner.goto(`/orgs/${organization.slug}/credentials`);
  await expect(owner.getByText("No credentials")).toBeVisible();
  await expect(owner.getByRole("alert").filter({ hasText: "Forbidden" })).toHaveCount(0);
});

test("a role you cannot change is shown rather than offered", async ({ owner, api, account }) => {
  const organization = await createOrganization(api, account);
  const member = await joinByInvitation(api, account, organization.id);

  await owner.goto(`/orgs/${organization.slug}/members`);

  // Nobody changes their own role — the server says to ask another owner or admin — so the
  // control is not there to be refused. It used to be, and the refusal arrived as a toast
  // after the fact.
  await expect(owner.getByLabel(`Role for ${account.email}`)).toHaveCount(0);

  // Somebody you outrank still gets one.
  await expect(owner.getByLabel(`Role for ${member.email}`)).toBeVisible();
});
