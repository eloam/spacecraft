import { test, expect } from "./fixtures";
import { UNPUBLISHABLE_VERSION } from "./workspace";

/**
 * The journey somebody actually makes on their first day, through the interface, with nothing
 * seeded: an organization, a project, a document, a target, a run.
 *
 * The one long test in this suite. It is long on purpose — the value is in the sequence, and
 * splitting it would mean seeding four times what it builds once.
 */
test("an organization can be taken from empty to a finished run", async ({ owner }) => {
  // Unique, because a slug names one organization across the whole installation: a fixed name
  // here passes once and collides with itself on the next run.
  const name = `Ledger ${crypto.randomUUID().slice(0, 8)}`;
  let organizationPath = "";

  await owner.goto("/");

  await test.step("create the organization", async () => {
    await owner.getByRole("button", { name: "New organization" }).first().click();
    await owner.getByLabel("Name").fill(name);
    await owner.getByRole("dialog").getByRole("button", { name: "Create" }).click();

    await expect(owner).toHaveURL(/\/orgs\/ledger-[a-z0-9]+$/);
    organizationPath = new URL(owner.url()).pathname;
  });

  await test.step("create the project", async () => {
    await owner.getByRole("button", { name: "New project" }).first().click();
    await owner.getByLabel("Name").fill("Ledger API");
    await owner.getByRole("dialog").getByRole("button", { name: "Create" }).click();

    await owner.getByRole("link", { name: /Ledger API/ }).first().click();
    await expect(owner).toHaveURL(`${organizationPath}/projects/ledger-api`);
  });

  await test.step("upload the document", async () => {
    await owner.locator('input[type="file"]').setInputFiles({
      name: "openapi.yaml",
      mimeType: "application/yaml",
      buffer: Buffer.from(UNPUBLISHABLE_VERSION),
    });

    // The upload is listed by the version it declares, which is the one npm cannot express.
    await expect(owner.getByText("1.2.3.4").first()).toBeVisible();
  });

  await test.step("add the SDK target", async () => {
    await owner.getByRole("link", { name: "SDK targets" }).click();
    await owner.getByRole("button", { name: "Add target" }).first().click();

    await owner.getByLabel("Package name").fill("ledger-sdk");
    await owner.getByRole("dialog").getByRole("button", { name: "Add target" }).click();

    await expect(owner.getByText("ledger-sdk").first()).toBeVisible();
  });

  await test.step("queue the run, and let the runner finish it", async () => {
    await owner.getByRole("button", { name: "Generate SDK" }).click();
    await owner.getByRole("dialog").getByRole("button", { name: "Queue run" }).click();

    await expect(owner).toHaveURL(new RegExp(`${organizationPath}/runs/[0-9a-f-]+$`));

    // A different process picks this up, fails it at validation because npm cannot take a
    // four-part version, and writes the result back. Nothing here polls anything: the page
    // does, and this waits for what it shows.
    await expect(owner.getByText("Failed").first()).toBeVisible({ timeout: 45_000 });
  });

});
