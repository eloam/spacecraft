import { test, expect } from "./fixtures";
import { REGISTRY_URL } from "./config";
import {
  PUBLISHABLE,
  createCredential,
  createOrganization,
  createProject,
  createTarget,
  queueRun,
  revokeCredential,
  uploadSpec,
  waitForRunToFinish,
} from "./workspace";

/**
 * Revoking a credential stops the packages it was pushing.
 *
 * The interesting part is where it stops. The SDK is still generated and still compiles —
 * nothing about the document changed — and the refusal comes at the last step, from a lookup
 * that asks whether the credential is usable rather than loading it and judging it after.
 * A run that failed earlier, or that published anyway, would both be wrong in ways the
 * outcome alone does not distinguish.
 */
test("revoking the credential stops the publish, and nothing else", { tag: "@slow" }, async ({
  owner,
  api,
  account,
}) => {
  test.setTimeout(600_000);

  const packageName = `@e2e/revoked-${crypto.randomUUID().slice(0, 8)}`;

  const organization = await createOrganization(api, account);
  const project = await createProject(api, account, organization.id);
  const specVersionId = await uploadSpec(api, account, organization.id, project.id, PUBLISHABLE);
  const credentialId = await createCredential(api, account, organization.id);
  const sdkTargetId = await createTarget(
    api,
    account,
    organization.id,
    project.id,
    packageName,
    credentialId,
  );

  // Revoked before the run rather than during it: what is under test is the state the publish
  // step finds, not a race with it.
  await revokeCredential(api, account, organization.id, credentialId);

  const runId = await queueRun(api, account, organization.id, specVersionId, sdkTargetId, true);
  const failureReason = await waitForRunToFinish(api, account, organization.id, runId, {
    timeout: 300_000,
  });

  expect(failureReason).toContain("has been revoked");

  await owner.goto(`/orgs/${organization.slug}/runs/${runId}`);

  // The work was done. Only the push was refused.
  await expect(owner.getByText(/revoked, has expired, or no longer exists/i)).toBeVisible();
  await expect(owner.getByText("Succeeded")).not.toHaveCount(0);

  // And nothing reached the registry under that name.
  const published = await api.get(`${REGISTRY_URL}/${encodeURIComponent(packageName)}`);

  expect(published.status(), "the package should not be on the registry").toBe(404);
});
