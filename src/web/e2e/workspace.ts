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

/** A document nothing objects to, and that an SDK can be generated and built from. */
export const PUBLISHABLE = `openapi: 3.0.3
info:
  title: Ledger API
  version: 2.0.0
  description: Ledgers and the entries in them.
  license:
    name: MIT
servers:
  - url: https://api.example.test
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
  schemas:
    Ledger:
      type: object
      properties:
        id:
          type: string
        name:
          type: string
security:
  - bearerAuth: []
paths:
  /ledgers:
    get:
      operationId: listLedgers
      summary: List every ledger.
      responses:
        '200':
          description: The ledgers.
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Ledger'
`;

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
  registryCredentialId?: string,
): Promise<string> {
  const response = await api.post(
    `/api/v1/organizations/${organizationId}/projects/${projectId}/targets`,
    {
      data: { language: 1, packageName, autoPublish: false, registryCredentialId },
      ...as(account),
    },
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
  publish = false,
): Promise<string> {
  const response = await api.post(`/api/v1/organizations/${organizationId}/runs`, {
    data: { specVersionId, sdkTargetId, publish },
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
  attempts = 120,
): Promise<string> {
  for (let attempt = 0; attempt < attempts; attempt++) {
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

/**
 * A token for the registry in the stack next door.
 *
 * Verdaccio publishes anonymously here, so the value is arbitrary — what matters is that a
 * credential exists, that it is encrypted on the way in, and that the runner can read it back
 * on the other side of the database.
 */
export async function createCredential(
  api: APIRequestContext,
  account: Account,
  organizationId: string,
  registryUrl = "http://verdaccio:4873",
): Promise<string> {
  const response = await api.post(`/api/v1/organizations/${organizationId}/credentials`, {
    data: {
      registry: 1,
      label: "Verdaccio",
      token: `e2e-${crypto.randomUUID()}`,
      username: null,
      expiresAt: null,
      registryUrl,
    },
    ...as(account),
  });

  expect(response.ok(), `creating a credential: ${response.status()}`).toBeTruthy();
  return (await response.json()).id;
}
