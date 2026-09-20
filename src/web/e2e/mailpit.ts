import { expect, type APIRequestContext } from "@playwright/test";
import { MAILPIT_URL } from "./config";

/**
 * Reads the mailbox the stack sends to.
 *
 * The invitation flow puts its whole grant in an email and stores only a hash, which is the
 * point of it — so the only way to test acceptance the way a recipient experiences it is to
 * open the message.
 */
type Message = { ID: string; Subject: string; To: { Address: string }[] };

export async function waitForMessage(
  api: APIRequestContext,
  to: string,
  subjectContains: string,
): Promise<string> {
  let match: Message | undefined;

  // Matched on the subject as well as the address: registering already sends a welcome to the
  // same mailbox, and waiting on the address alone hands back whichever arrived first.
  await expect(async () => {
    const response = await api.get(`${MAILPIT_URL}/api/v1/messages?limit=100`);
    const messages: Message[] = (await response.json()).messages ?? [];

    match = messages.find(
      (m) =>
        m.To.some((recipient) => recipient.Address.toLowerCase() === to.toLowerCase()) &&
        m.Subject.toLowerCase().includes(subjectContains.toLowerCase()),
    );

    expect(match, `a message matching "${subjectContains}" reaching ${to}`).toBeDefined();
  }).toPass({ timeout: 30_000, intervals: [500] });

  if (!match) {
    throw new Error(`no message matching "${subjectContains}" ever reached ${to}`);
  }

  const body = await api.get(`${MAILPIT_URL}/api/v1/message/${match.ID}`);
  return (await body.json()).Text ?? "";
}

/**
 * The first link in a message, parsed.
 *
 * A test opens its path against the base URL or reads a token off its query; the origin is
 * whatever the stack was told to print into mail, which is not necessarily where the browser
 * under test is pointed.
 */
export function linkIn(body: string): URL {
  const match = /https?:\/\/\S+/.exec(body);
  if (!match) {
    throw new Error("the message carries no link");
  }

  return new URL(match[0]);
}
