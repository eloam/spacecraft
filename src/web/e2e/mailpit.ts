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
  // Matched on the subject as well as the address: registering already sends a welcome to the
  // same mailbox, and waiting on the address alone hands back whichever arrived first.
  for (let attempt = 0; attempt < 60; attempt++) {
    const response = await api.get(`${MAILPIT_URL}/api/v1/messages?limit=100`);
    const messages: Message[] = (await response.json()).messages ?? [];

    const match = messages.find(
      (m) =>
        m.To.some((recipient) => recipient.Address.toLowerCase() === to.toLowerCase()) &&
        m.Subject.toLowerCase().includes(subjectContains.toLowerCase()),
    );

    if (match) {
      const body = await api.get(`${MAILPIT_URL}/api/v1/message/${match.ID}`);
      return (await body.json()).Text ?? "";
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`no message matching "${subjectContains}" ever reached ${to}`);
}

/** The first link in a message, as a path this application can be told to open. */
export function linkIn(body: string): string {
  const match = /https?:\/\/\S+/.exec(body);
  expect(match, "the message carries a link").not.toBeNull();

  const url = new URL(match![0]);
  return `${url.pathname}${url.search}`;
}
