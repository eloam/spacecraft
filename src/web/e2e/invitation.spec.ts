import { test, expect, anEmail, register, signIn } from "./fixtures";
import { createOrganization } from "./workspace";
import { linkIn, waitForMessage } from "./mailpit";

/**
 * Joining an organization, the way the person joining experiences it: an address is invited,
 * a message arrives, and whoever can read that mailbox — in their own browser, with their own
 * account — becomes a member by opening the link.
 */
test("an invitation is sent, read from the mailbox and accepted", async ({
  owner,
  api,
  account,
  browser,
}) => {
  // Named by the helper, which makes it unique: a slug belongs to one organization
  // across the whole installation, so a fixed name here collides with the last run.
  const organization = await createOrganization(api, account);
  const inviteeEmail = anEmail("invitee");

  await owner.goto(`/orgs/${organization.slug}/members`);
  await owner.getByRole("button", { name: "Invite someone" }).click();
  await owner.getByLabel("Email").fill(inviteeEmail);
  await owner.getByRole("dialog").getByRole("button", { name: "Send invitation" }).click();

  // Accepted, not created: nobody has joined anything yet.
  await expect(owner.getByText(`Invitation sent to ${inviteeEmail}`)).toBeVisible();
  await expect(owner.getByRole("row")).toHaveCount(2, { timeout: 5_000 });

  const body = await waitForMessage(api, inviteeEmail, "invited");
  const acceptPath = linkIn(body);

  // A second browser, a second account, the same link — which is the whole point: the grant
  // travelled to a mailbox, not to an account.
  const context = await browser.newContext();
  const invitee = await context.newPage();
  await signIn(invitee, await register(api, inviteeEmail));

  await invitee.goto(acceptPath);
  await expect(invitee).toHaveURL("/");
  await expect(invitee.getByText(organization.name)).toBeVisible();

  await owner.reload();
  await expect(owner.getByText(inviteeEmail)).toBeVisible();

  await context.close();
});
