import { test, expect, as } from "./fixtures";
import { createOrganization, joinByInvitation, queuedRun, waitForRunToFinish } from "./workspace";

/**
 * A run finishes in the runner and the person who started it is told in the browser. Two
 * processes, one database and a sweep in between — the one path in this application that no
 * single suite can cover on its own.
 */
test("a finished run reaches the bell, and opens from it", async ({ owner, api, account }) => {
  const { organization, runId } = await queuedRun(api, account);
  await waitForRunToFinish(api, account, organization.id, runId);

  await owner.goto("/");

  const bell = owner.getByRole("button", { name: /^Notifications/ });
  await bell.click();

  const notification = owner.getByRole("button", { name: /Dismiss "Run failed/ });
  await expect(notification).toBeVisible({ timeout: 30_000 });

  await owner.getByText("Run failed — ledger-sdk").click();
  await expect(owner).toHaveURL(`/orgs/${organization.slug}/runs/${runId}`);

  // Opening one reads it: the count beside the bell is gone.
  await expect(bell).toHaveAttribute("aria-label", "Notifications");
});

test("clearing asks before it loses something unread", async ({ owner, api, account }) => {
  const { organization, runId } = await queuedRun(api, account);
  await waitForRunToFinish(api, account, organization.id, runId);

  await owner.goto("/");
  await owner.getByRole("button", { name: /^Notifications/ }).click();
  await expect(owner.getByText("Run failed — ledger-sdk")).toBeVisible({ timeout: 30_000 });

  await owner.getByRole("button", { name: "Clear all" }).click();

  // Unread, so the dialog stands in the way and says how much is at stake.
  const dialog = owner.getByRole("dialog");
  await expect(dialog).toContainText("you have not read yet");

  // By role: the label also names the hidden input Base UI keeps for form participation.
  await dialog.getByRole("switch", { name: "Remove unread as well" }).click();
  await dialog.getByRole("button", { name: "Clear" }).click();

  await owner.getByRole("button", { name: /^Notifications/ }).click();
  await expect(owner.getByText("Nothing yet.")).toBeVisible();
});

test("being removed reaches the person it happened to, who is no longer a member", async ({
  api,
  account,
  pageAs,
}) => {
  const organization = await createOrganization(api, account);
  const member = await joinByInvitation(api, account, organization.id);

  const theirs = await pageAs(member);

  const removed = await api.delete(
    `/api/v1/organizations/${organization.id}/members/${member.user.id}`,
    as(account),
  );

  await expect(removed, "removing a member").toBeOK();

  await theirs.goto("/");

  // Gone from their organizations, which is the point of the removal. That one was the only
  // organization they had, so what the list renders once it has loaded is its empty state —
  // and only then does a missing link mean anything, since a list still loading has none.
  await expect(theirs.getByText("No organizations yet")).toBeVisible();
  await expect(theirs.getByRole("link", { name: organization.name })).toHaveCount(0);

  // ...and told anyway, which is the point of the notification: every other item in this list
  // is filtered to organizations they still belong to, and this one cannot be.
  await theirs.getByRole("button", { name: /^Notifications/ }).click();
  await expect(theirs.getByText(`You were removed from ${organization.name}`)).toBeVisible();

  // Naming the account that did it, not only the name it goes by.
  await expect(theirs.getByText(account.email, { exact: false })).toBeVisible();
});
