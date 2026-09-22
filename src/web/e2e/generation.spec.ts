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
test(
  "a document becomes an SDK that can be downloaded",
  { tag: "@slow" },
  async ({ owner, api, account }) => {
    // Generating, installing and compiling, on a cold registry cache, against whatever npmjs
    // is doing today.
    test.setTimeout(600_000);

    const organization = await createOrganization(api, account);
    const project = await createProject(api, account, organization.id);
    const specVersionId = await uploadSpec(api, account, organization.id, project.id, PUBLISHABLE);
    const sdkTargetId = await createTarget(api, account, organization.id, project.id, "ledger-sdk");
    const runId = await queueRun(api, account, organization.id, specVersionId, sdkTargetId);

    const failureReason = await waitForRunToFinish(api, account, organization.id, runId, {
      timeout: 300_000,
    });

    expect(failureReason, "the run should have finished without a failure").toBe("");

    await owner.goto(`/orgs/${organization.slug}/runs/${runId}`);

    // Every step, not just the outcome: a run that skipped generation would still say
    // Succeeded if publishing was the only thing left to do. So each row of the Steps card,
    // in pipeline order, with the status it shows next to its name — Publish is the one step
    // this run was not asked for, and the page says so rather than counting it as done.
    //
    // The rows are shadcn Items, which carry no role of their own; data-slot is shadcn's own
    // hook on its parts, so it is a steadier handle than a class. Scoped to the Steps card, so
    // the count is about the steps and not about whatever else the page renders as an Item.
    const steps = owner
      .locator("[data-slot=card]", { has: owner.getByText("Steps", { exact: true }) })
      .locator("[data-slot=item]");
    await expect(steps).toHaveText([
      /^Validate\s*Succeeded/,
      /^Generate\s*Succeeded/,
      /^Build\s*Succeeded/,
      /^Publish\s*Skipped/,
    ]);

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
  },
);
