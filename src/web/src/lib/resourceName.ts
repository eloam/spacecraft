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

/** What the slug will become, so the address can be shown before it changes. */
export function slugPreview(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}
