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
  // The members table, not every row on the page: the pending invitation now has a
  // table of its own underneath.
  await expect(owner.getByRole("table").first().getByRole("row")).toHaveCount(2);

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

test("an invitation can be taken back before it is used", async ({
  owner,
  api,
  account,
  browser,
}) => {
  const organization = await createOrganization(api, account);
  const inviteeEmail = anEmail("withdrawn");

  await owner.goto(`/orgs/${organization.slug}/members`);
  await owner.getByRole("button", { name: "Invite someone" }).click();
  await owner.getByLabel("Email").fill(inviteeEmail);
  await owner.getByRole("dialog").getByRole("button", { name: "Send invitation" }).click();

  // Listed, which for a long time it was not: the API could list and withdraw them and
  // nothing showed them, so a mistyped address could only be waited out.
  await expect(owner.getByText("Pending invitations")).toBeVisible();

  // In the table, not in the toast that also names the address.
  await expect(owner.getByRole("table").nth(1).getByText(inviteeEmail)).toBeVisible();

  // Captured before withdrawing, so the link under test is a real one.
  const acceptPath = linkIn(await waitForMessage(api, inviteeEmail, "invited"));

  await owner
    .getByRole("button", { name: `Withdraw the invitation to ${inviteeEmail}` })
    .click();

  await expect(owner.getByText("Pending invitations")).toBeHidden();

  // And the link is out, not merely hidden from the person who sent it.
  const context = await browser.newContext();
  const invitee = await context.newPage();
  await signIn(invitee, await register(api, inviteeEmail));

  await invitee.goto(acceptPath);
  await expect(invitee.getByText("This invitation cannot be used")).toBeVisible();

  await context.close();
});
