import { defineConfig, devices } from "@playwright/test";
import { API_URL, WEB_URL } from "./e2e/config";

/**
 * End-to-end tests: the browser, the API, the database and the runner, with nothing stubbed.
 *
 * They exist for what the other suites cannot see. The integration tests prove the API
 * answers correctly and the unit tests prove the rules are right, but neither has ever opened
 * the application — every claim about a form, a link or a dialog has until now been checked by
 * hand and pinned by nothing.
 *
 * Deliberately few. A browser test costs a hundred times what an integration test costs, so
 * these cover the paths that cross a boundary — sign-up, an invitation through a real mailbox,
 * a run that travels to another process and comes back as a notification — and leave
 * everything else to the suites that can afford to be thorough.
 */
export default defineConfig({
  testDir: "./e2e",
  globalSetup: "./e2e/global-setup.ts",

  // Every test registers its own account and works inside its own organization, so there is
  // no shared state to serialise around and no table to truncate between tests.
  fullyParallel: true,

  forbidOnly: Boolean(process.env.CI),
  // One retry on a build agent, none locally: a flake there costs a rerun of everything,
  // and a flake here should be looked at rather than hidden.
  retries: process.env.CI ? 1 : 0,
  // Two, everywhere. The tests are parallel-safe — each builds its own organization — but
  // they share one API container and one database, so more browsers mostly queue on the same
  // process. Playwright's default of half the cores buys nothing here and, on a machine also
  // running the development stack, starves what it is testing.
  workers: 2,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : [["list"]],

  // Generous, and deliberately so: these tests wait on another process picking work off a
  // queue, and a build agent is not a quiet machine. A tight timeout here buys flakes, not
  // speed — the suite's cost is the browser, not the waiting.
  timeout: 120_000,
  expect: { timeout: 20_000 },

  use: {
    baseURL: WEB_URL,
    // Kept only where it earns its size: a trace of the attempt that failed, and of nothing
    // that passed.
    // Bounded, so a locator that names nothing fails saying so rather than quietly eating
    // the test's whole budget and reporting whatever else was still pending.
    actionTimeout: 20_000,
    trace: "on-first-retry",
    video: "retain-on-failure",
    screenshot: "only-on-failure",
  },

  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],

  // The built application, not the dev server: what ships is what should be tested, and Vite
  // serves them differently enough to matter.
  webServer: {
    // Built here rather than by the script that calls this, and never reused. `vite preview`
    // serves whatever is in dist: a server left running from an earlier campaign happily
    // serves the bundle from before the change under test, and the run passes or fails on
    // code nobody is looking at. Four seconds a run to make that impossible.
    command: "npm run build && npx vite preview --port 4173 --strictPort",
    url: WEB_URL,
    reuseExistingServer: false,
    timeout: 120_000,
    env: { API_TARGET: API_URL },
  },
});
