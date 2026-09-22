import { test, expect } from "./fixtures";
import { REGISTRY_URL } from "./config";
import {
  PUBLISHABLE,
  createCredential,
  createOrganization,
  createProject,
  createTarget,
  queueRun,
  uploadSpec,
  waitForRunToFinish,
} from "./workspace";

/**
 * The promise the product makes, kept all the way to somebody else's server.
 *
 * The generation test stops at a tarball in our own storage. This one goes one step further,
 * and it is the step that carries the most: the publish token is written encrypted, read back
 * by a different process through the shared key ring, and handed to npm. Nothing else here
 * exercises that path, and its failure mode is a run that says it published nothing.
 *
 * The last assertion is the only one in this suite that asks something outside the
 * application: the registry itself, whether the package is there.
 */
test(
  "a run publishes the SDK to a registry, and the registry has it",
  { tag: "@slow" },
  async ({ owner, api, account }) => {
    test.setTimeout(600_000);

    // Scoped and unique. A registry is append-only: the same name and version twice is a
    // no-op the pipeline treats as satisfied, which would let a broken publish pass.
    const packageName = `@e2e/ledger-${crypto.randomUUID().slice(0, 8)}`;

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

    const runId = await queueRun(api, account, organization.id, specVersionId, sdkTargetId, true);

    const failureReason = await waitForRunToFinish(api, account, organization.id, runId, {
      timeout: 300_000,
    });
    expect(failureReason, "the run should have published without a failure").toBe("");

    await owner.goto(`/orgs/${organization.slug}/publications`);
    await expect(owner.getByText(packageName)).toBeVisible();
    await expect(owner.getByText("2.0.0").first()).toBeVisible();

    // And the registry agrees. Everything above is this application describing its own work;
    // this is the one question asked of somebody else.
    const published = await api.get(`${REGISTRY_URL}/${encodeURIComponent(packageName)}`);

    await expect(published, `${packageName} on the registry`).toBeOK();
    expect(Object.keys((await published.json()).versions)).toContain("2.0.0");
  },
);
