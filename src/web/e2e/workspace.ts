import { expect, type APIRequestContext } from "@playwright/test";
import { anEmail, as, register, type Account } from "./fixtures";
import { linkIn, waitForMessage } from "./mailpit";

/**
 * The pieces a test needs in place before it can test anything, built through the API.
 *
 * Doing this through the interface would make every test a copy of the one that already walks
 * that path, and ten times slower for it.
 */
export type Organization = { id: string; slug: string; name: string };
export type Project = { id: string; slug: string; name: string };

export async function createOrganization(
  api: APIRequestContext,
  account: Account,
  name = `E2E ${crypto.randomUUID().slice(0, 8)}`,
): Promise<Organization> {
  const response = await api.post("/api/v1/organizations", { data: { name }, ...as(account) });
  expect(response.ok(), `creating an organization: ${response.status()}`).toBeTruthy();
  return response.json();
}

export async function createProject(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  name = "Ledger API",
): Promise<Project> {
  const response = await api.post(`/api/v1/organizations/${organizationId}/projects`, {
    data: { name },
    ...as(account),
  });

  expect(response.ok(), `creating a project: ${response.status()}`).toBeTruthy();
  return response.json();
}

/**
 * A document npm cannot express a version for.
 *
 * Four parts is ordinary for NuGet and inexpressible in npm, so a run against an npm target
 * fails at its first step — which is the point: the whole path from the browser to the runner
 * and back is exercised without an SDK actually being generated and built.
 */
export const UNPUBLISHABLE_VERSION = `openapi: 3.0.3
info:
  title: Ledger API
  version: 1.2.3.4
  description: Ledgers and entries.
  license:
    name: MIT
servers:
  - url: https://api.example.test
paths:
  /ledgers:
    get:
      operationId: listLedgers
      responses:
        '200':
          description: ok
`;

export async function uploadSpec(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  projectId: string,
  document = UNPUBLISHABLE_VERSION,
): Promise<string> {
  const response = await api.post(
    `/api/v1/organizations/${organizationId}/projects/${projectId}/specs`,
    {
      multipart: {
        file: {
          name: "openapi.yaml",
          mimeType: "application/yaml",
          buffer: Buffer.from(document),
        },
      },
      ...as(account),
    },
  );

  expect(response.ok(), `uploading a specification: ${response.status()}`).toBeTruthy();
  return (await response.json()).specVersion.id;
}

export async function createTarget(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  projectId: string,
  packageName = "ledger-sdk",
): Promise<string> {
  const response = await api.post(
    `/api/v1/organizations/${organizationId}/projects/${projectId}/targets`,
    { data: { language: 1, packageName, autoPublish: false }, ...as(account) },
  );

  expect(response.ok(), `creating a target: ${response.status()}`).toBeTruthy();
  return (await response.json()).id;
}

export async function queueRun(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  specVersionId: string,
  sdkTargetId: string,
): Promise<string> {
  const response = await api.post(`/api/v1/organizations/${organizationId}/runs`, {
    data: { specVersionId, sdkTargetId, publish: false },
    ...as(account),
  });

  expect(response.ok(), `queueing a run: ${response.status()}`).toBeTruthy();
  return (await response.json()).id;
}

/** Everything needed for a run, queued and left to the runner. */
export async function queuedRun(api: APIRequestContext, account: Account) {
  const organization = await createOrganization(api, account);
  const project = await createProject(api, account, organization.id);
  const specVersionId = await uploadSpec(api, account, organization.id, project.id);
  const sdkTargetId = await createTarget(api, account, organization.id, project.id);
  const runId = await queueRun(api, account, organization.id, specVersionId, sdkTargetId);

  return { organization, project, runId };
}

/** Waits for the runner to pick a run up and finish it, without watching a page do it. */
export async function waitForRunToFinish(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  runId: string,
): Promise<string> {
  for (let attempt = 0; attempt < 120; attempt++) {
    const response = await api.get(
      `/api/v1/organizations/${organizationId}/runs/${runId}`,
      as(account),
    );

    expect(response.ok(), `reading a run: ${response.status()}`).toBeTruthy();

    // 3 succeeded, 4 failed, 5 cancelled — see SpaceCraft.Domain.Pipelines.RunStatus.
    const run = await response.json();
    if ([3, 4, 5].includes(run.status)) {
      return run.failureReason ?? "";
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`run ${runId} never finished — is the runner in compose.e2e.yaml running?`);
}

/**
 * Puts a second person in an organization, the only way there is: an invitation, a mailbox and
 * a token. Membership cannot be granted any other way, which is the point of that flow — so a
 * test that needs a colleague pays for one.
 */
export async function joinByInvitation(
  api: APIRequestContext,
  inviter: Account,
  organizationId: string,
  role = 2,
): Promise<Account> {
  const email = anEmail("member");

  const invited = await api.post(`/api/v1/organizations/${organizationId}/members`, {
    data: { email, role },
    ...as(inviter),
  });

  expect(invited.ok(), `inviting ${email}: ${invited.status()}`).toBeTruthy();

  const account = await register(api, email);
  const token = new URL(`http://x${linkIn(await waitForMessage(api, email, "invited"))}`).searchParams.get("token");

  const accepted = await api.post("/api/v1/invitations/accept", {
    data: { token },
    ...as(account),
  });

  expect(accepted.ok(), `accepting an invitation: ${accepted.status()}`).toBeTruthy();
  return account;
}
