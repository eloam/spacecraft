import { z } from "zod";

/**
 * Mirrors SpaceCraft.Domain.Common.ResourceName.
 *
 * Duplicated deliberately rather than derived: the server is the authority and rejects
 * anything invalid, but a name typed into a form should be corrected before a round trip,
 * and the preview below needs the same rules to be honest about the URL it promises.
 */
export const NAME_RULE = "Use letters, numbers, spaces and hyphens only, starting with a letter or number.";

export const NAME_MAX_LENGTH = 100;

const allowed = /^[A-Za-z0-9][A-Za-z0-9 -]*$/;

export function isValidResourceName(name: string): boolean {
  const trimmed = name.trim();
  return trimmed.length > 0 && trimmed.length <= NAME_MAX_LENGTH && allowed.test(trimmed);
}

/**
 * The name as a form declares it: trimmed, since that is how the server stores it, with
 * the length said on its own so a name that is only too long is told so rather than given
 * the whole rule.
 */
export const resourceNameField = z
  .string()
  .trim()
  .max(NAME_MAX_LENGTH, `Use ${NAME_MAX_LENGTH} characters or fewer.`)
  .refine(isValidResourceName, NAME_RULE);

/** What the slug will become, so it can be shown before it changes. */
export function slugPreview(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}
