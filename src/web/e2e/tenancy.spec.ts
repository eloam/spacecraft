import { test, expect, register } from "./fixtures";
import { createOrganization } from "./workspace";

/**
 * A link to somebody else's organization, opened by somebody who is not in it.
 *
 * The API answers 404 rather than 403 on purpose — a refusal that distinguishes the two
 * confirms which organization ids are real. What matters here is the other half: that the
 * interface says something rather than sitting on a spinner for ever.
 */
test("an organization you are not in shows you nothing, and says so", async ({
  api,
  account,
  pageAs,
}) => {
  const organization = await createOrganization(api, account);

  const stranger = await register(api);
  const theirs = await pageAs(stranger);

  await theirs.goto(`/orgs/${organization.slug}/members`);

  // Not the members, and not a spinner: something a person can act on.
  await expect(theirs.getByText("Organization not found")).toBeVisible();
  await expect(theirs.getByRole("table")).toHaveCount(0);

  // The same by id, which a notification or an email link carries. An id used to skip the
  // lookup the slug went through, and with it the check: the shell rendered with the guid
  // for a name and the sections around each page's own refusal.
  await theirs.goto(`/orgs/${organization.id}/members`);

  await expect(theirs.getByText("Organization not found")).toBeVisible();
  await expect(theirs.getByRole("table")).toHaveCount(0);
});

test("a project that is not there says so rather than loading for ever", async ({
  owner,
  api,
  account,
}) => {
  const organization = await createOrganization(api, account);

  await owner.goto(`/orgs/${organization.slug}/projects/no-such-project`);

  await expect(owner.getByText("Project not found")).toBeVisible();
});

test("an address nobody declared is said, not swallowed", async ({ owner }) => {
  await owner.goto("/nowhere/at/all");

  // Said in place, with the address left in the bar to correct: a redirect to the dashboard
  // turned every typo into a silent arrival somewhere else.
  await expect(owner.getByText("Page not found")).toBeVisible();
  await expect(owner).toHaveURL(/\/nowhere\/at\/all$/);
});
