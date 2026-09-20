import { z } from "zod";

/**
 * The shape rules the forms share, written once.
 *
 * Mirrors of the API's validators, kept so a form corrects a value before a round trip: the
 * server is the authority and rejects the same things, and its wording is what these copy.
 * Three forms used to declare the password rule and four the address, and the display name
 * was allowed 80 characters in one form and 100 in another while the server allows 100 —
 * measured trimmed, which is how every rule here measures too.
 */
export const emailField = z.string().trim().email("Enter a valid email address.");

/** Identity's password policy: RequiredLength = 12, in SpaceCraft.Infrastructure. */
export const newPasswordField = z.string().min(12, "Use at least 12 characters.");

/** RegisterRequest and UpdateProfileRequest: trimmed, at most 100 characters. */
export const displayNameField = z.string().trim().max(100, "Use 100 characters or fewer.");
