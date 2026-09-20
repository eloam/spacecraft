import { test, expect, anEmail, register } from "./fixtures";
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
  pageAs,
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
  const accept = linkIn(body);

  // A second browser, a second account, the same link — which is the whole point: the grant
  // travelled to a mailbox, not to an account.
  const invitee = await pageAs(await register(api, inviteeEmail));

  // Read before it is taken: the link opens an offer, not a membership.
  await invitee.goto(accept.pathname + accept.search);
  await expect(invitee.getByRole("heading", { name: `Join ${organization.name}` })).toBeVisible();
  // The name the inviter goes by, which is what the preview shows — putting their
  // address in front of somebody who has not joined yet would be a different decision.
  await expect(invitee.getByText(account.email.split("@")[0])).toBeVisible();

  await invitee.getByRole("button", { name: `Join ${organization.name}` }).click();
  await expect(invitee).toHaveURL(`/orgs/${organization.slug}`);

  await owner.reload();
  await expect(owner.getByText(inviteeEmail)).toBeVisible();
});

test("an invitation can be taken back before it is used", async ({
  owner,
  api,
  account,
  pageAs,
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
  const accept = linkIn(await waitForMessage(api, inviteeEmail, "invited"));

  await owner
    .getByRole("button", { name: `Withdraw the invitation to ${inviteeEmail}` })
    .click();

  await expect(owner.getByText("Pending invitations")).toBeHidden();

  // And the link is out, not merely hidden from the person who sent it.
  const invitee = await pageAs(await register(api, inviteeEmail));

  await invitee.goto(accept.pathname + accept.search);
  await expect(invitee.getByText("This invitation cannot be used")).toBeVisible();
});

test("an invitation can be turned down, and the link stops working", async ({
  owner,
  api,
  account,
  pageAs,
}) => {
  const organization = await createOrganization(api, account);
  const inviteeEmail = anEmail("declines");

  await owner.goto(`/orgs/${organization.slug}/members`);
  await owner.getByRole("button", { name: "Invite someone" }).click();
  await owner.getByLabel("Email").fill(inviteeEmail);
  await owner.getByRole("dialog").getByRole("button", { name: "Send invitation" }).click();

  const accept = linkIn(await waitForMessage(api, inviteeEmail, "invited"));

  const invitee = await pageAs(await register(api, inviteeEmail));

  await invitee.goto(accept.pathname + accept.search);
  await invitee.getByRole("button", { name: "Decline" }).click();

  await expect(invitee.getByRole("heading", { name: "Invitation declined" })).toBeVisible();

  // Out for good: opening the same link again offers nothing.
  await invitee.goto(accept.pathname + accept.search);
  await expect(invitee.getByText("This invitation cannot be used")).toBeVisible();

  // And it stops being offered as pending. It used to stay in that list with a link that no
  // longer worked, because the list carried its own copy of "still open".
  //
  // Waited on the invitations response itself: the heading is just as hidden while the page
  // is a skeleton, and the list is only asked for once the members have answered, so no
  // element on the page says "the list is in and empty" — the response does.
  const invitations = owner.waitForResponse(
    (response) => response.url().includes("/members/invitations") && response.ok(),
  );
  await owner.reload();
  await invitations;
  await expect(owner.getByText("Pending invitations")).toBeHidden();

  // The organization is told, rather than left waiting for an answer that already came.
  await owner.getByRole("button", { name: /^Notifications/ }).click();
  await expect(owner.getByText(`${inviteeEmail} declined`)).toBeVisible();
});
