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
});
