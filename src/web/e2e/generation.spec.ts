import { test, expect } from "./fixtures";
import {
  PUBLISHABLE,
  createOrganization,
  createProject,
  createTarget,
  queueRun,
  uploadSpec,
  waitForRunToFinish,
} from "./workspace";

/**
 * The thing the product is for: a document goes in, an SDK comes out.
 *
 * Every other test in this suite queues a run that fails at validation, which is enough to
 * prove the path between the browser, the API and the runner. This one lets the pipeline
 * finish the job — Kiota generates the client, npm resolves its runtime packages from
 * Verdaccio, TypeScript compiles it, and a tarball lands in object storage.
 *
 * It is the slowest test here by an order of magnitude, and the only one that needs a
 * registry. That is the price of covering the four steps nothing else covers.
 */
test("a document becomes an SDK that can be downloaded", { tag: "@slow" }, async ({
  owner,
  api,
  account,
}) => {
  // Generating, installing and compiling, on a cold registry cache, against whatever npmjs
  // is doing today.
  test.setTimeout(600_000);

  const organization = await createOrganization(api, account);
  const project = await createProject(api, account, organization.id);
  const specVersionId = await uploadSpec(api, account, organization.id, project.id, PUBLISHABLE);
  const sdkTargetId = await createTarget(api, account, organization.id, project.id, "ledger-sdk");
  const runId = await queueRun(api, account, organization.id, specVersionId, sdkTargetId);

  const failureReason = await waitForRunToFinish(
    api,
    account,
    organization.id,
    runId,
    600,
  );

  expect(failureReason, "the run should have finished without a failure").toBe("");

  await owner.goto(`/orgs/${organization.slug}/runs/${runId}`);
  await expect(owner.getByText("Succeeded").first()).toBeVisible();

  // Every step, not just the outcome: a run that skipped generation would still say
  // Succeeded if publishing was the only thing left to do.
  await expect(owner.getByText("2.0.0").first()).toBeVisible();

  // And the artifact is really there. The download used to reach for the bucket directly,
  // which meant it worked in exactly one deployment; it goes through the API now, and this is
  // the only test that opens it.
  //
  // Asserted on the response rather than on a download event: the page fetches the bytes and
  // hands them to a synthetic anchor, which is a detail of how a blob reaches the disk. What
  // matters is that the API served the artifact to this session.
  const artifact = owner.waitForResponse(
    (response) => response.url().includes(`/runs/${runId}/artifact`) && response.status() === 200,
  );

  await owner.getByRole("button", { name: "Artifact" }).click();

  expect((await artifact).headers()["content-disposition"] ?? "").toContain(".tgz");
});
