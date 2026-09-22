import { test, expect } from "./fixtures";
import { createOrganization, createProject } from "./workspace";

/**
 * The id is how the API is addressed; the slug is how the client is. A link written for later
 * — in a notification, in an email — carries the id, because a name can be changed. Opening
 * one has to work, and has to stop showing a guid the moment the name is known.
 */
test("a link that names an organization by id becomes its slug", async ({
  owner,
  api,
  account,
}) => {
  const organization = await createOrganization(api, account);
  const project = await createProject(api, account, organization.id);

  await owner.goto(`/orgs/${organization.id}/projects/${project.id}`);

  await expect(owner).toHaveURL(`/orgs/${organization.slug}/projects/${project.slug}`);
  await expect(owner.getByRole("button", { name: "Switch organization" })).toContainText(
    organization.name,
  );
});
