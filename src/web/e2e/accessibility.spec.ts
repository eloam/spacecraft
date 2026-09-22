import AxeBuilder from "@axe-core/playwright";
import type { Locator, Page } from "@playwright/test";
import type { Organization, Project } from "../src/api/types";
import { test, expect } from "./fixtures";
import { createOrganization, createProject } from "./workspace";

/**
 * One axe pass per top-level page.
 *
 * The linter reads one file at a time, and a page is what several of them render together:
 * a heading level that skips one, two landmarks with the same name, a contrast that only
 * exists once the theme's colours are on the element. axe reads the rendered page with
 * everything on it — so it runs against the built application, in a workspace with one
 * organization and one project, which is enough to render every table, tile and form once.
 */

type Workspace = { organization: Organization; project: Project };

/**
 * Every signed-in page, and what it shows once its data has landed.
 *
 * The pass waits for that rather than for the h1: every page renders its heading above the
 * list it is still loading, so a pass timed on the heading grades a row of skeletons on one
 * run and the table on the next. The slugs are filled in per test, since each builds its own
 * workspace.
 */
const signedIn: { path: string; ready: (page: Page, workspace: Workspace) => Locator }[] = [
  {
    path: "/",
    ready: (page, { organization }) => page.getByRole("link", { name: organization.name }),
  },
  {
    path: "/orgs/{orgSlug}",
    ready: (page, { project }) => page.getByRole("link", { name: project.name }),
  },
  {
    path: "/orgs/{orgSlug}/projects/{projectSlug}",
    ready: (page) => page.getByText("Each upload is kept as a version"),
  },
  {
    path: "/orgs/{orgSlug}/projects/{projectSlug}/targets",
    ready: (page) => page.getByText("No SDK targets"),
  },
  { path: "/orgs/{orgSlug}/runs", ready: (page) => page.getByText("No runs yet") },
  {
    path: "/orgs/{orgSlug}/members",
    ready: (page) => page.getByRole("columnheader", { name: "Person" }),
  },
  { path: "/orgs/{orgSlug}/credentials", ready: (page) => page.getByText("No credentials") },
  {
    path: "/orgs/{orgSlug}/publications",
    ready: (page) => page.getByText("Nothing published yet"),
  },
  {
    path: "/orgs/{orgSlug}/settings",
    ready: (page) => page.getByRole("heading", { name: "General" }),
  },
  { path: "/account", ready: (page) => page.getByRole("heading", { name: "Profile" }) },
  { path: "/account/security", ready: (page) => page.getByText("This device") },
  {
    path: "/account/notifications",
    ready: (page) => page.getByRole("heading", { name: "In-app" }),
  },
  // The owner of an organization cannot delete their account, and the page says so once the
  // check has come back — the only state a signed-in test can put that page in.
  { path: "/account/danger", ready: (page) => page.getByText("Hand these over first") },
];

/** The two forms render in one go, so their heading is the whole page. */
const signedOut: { path: string; ready: (page: Page) => Locator }[] = [
  {
    path: "/login",
    ready: (page) => page.getByRole("heading", { level: 1, name: "Sign in to SpaceCraft" }),
  },
  {
    path: "/register",
    ready: (page) => page.getByRole("heading", { level: 1, name: "Create your account" }),
  },
];

function at(path: string, { organization, project }: Workspace): string {
  return path.replace("{orgSlug}", organization.slug).replace("{projectSlug}", project.slug);
}

/**
 * Fails on the violations themselves, not on `[…] is not []`: each one names its rule, how
 * bad it is, and the selectors it was found at, which is what someone fixing it reads first.
 */
async function expectNoViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();

  expect(
    results.violations,
    JSON.stringify(
      results.violations.map((v) => ({
        id: v.id,
        impact: v.impact,
        help: v.help,
        nodes: v.nodes.map((n) => n.target),
      })),
      null,
      2,
    ),
  ).toEqual([]);
}

// Both colour schemes: the theme follows the browser's, and contrast is the one thing that
// differs between the two — a red that reads on white can fall short on a dark card.
for (const colorScheme of ["light", "dark"] as const) {
  test.describe(colorScheme, () => {
    test.use({ colorScheme });

    for (const { path, ready } of signedIn) {
      test(path, async ({ owner, api, account }) => {
        const organization = await createOrganization(api, account);
        const project = await createProject(api, account, organization.id);
        const workspace = { organization, project };

        await owner.goto(at(path, workspace));
        await expect(ready(owner, workspace)).toBeVisible();

        await expectNoViolations(owner);
      });
    }

    test.describe("signed out", () => {
      // Signed out, on purpose: a session would redirect both pages away.
      test.use({ storageState: { cookies: [], origins: [] } });

      for (const { path, ready } of signedOut) {
        test(path, async ({ page }) => {
          await page.goto(path);
          await expect(ready(page)).toBeVisible();

          await expectNoViolations(page);
        });
      }
    });
  });
}
