import type { components } from "./schema";

/**
 * Convenience aliases over the generated OpenAPI schema.
 *
 * These types are derived from the running API rather than hand-written, so a change to a
 * response contract breaks the build here instead of at runtime in a user's browser.
 * Regenerate with `npm run gen:api`.
 */
type Schemas = components["schemas"];

export type User = Schemas["UserResponse"];
export type AuthResponse = Schemas["AuthResponse"];
export type Organization = Schemas["OrganizationResponse"];
export type Project = Schemas["ProjectResponse"];
export type SdkTarget = Schemas["TargetResponse"];
export type SpecVersion = Schemas["SpecVersionResponse"];
export type SpecList = Schemas["SpecListResponse"];
export type SpecUpload = Schemas["SpecUploadResponse"];
export type SpecDiagnostic = Schemas["SpecDiagnostic"];
export type SpecContent = Schemas["SpecContentResponse"];
export type Run = Schemas["RunResponse"];
export type Credential = Schemas["CredentialResponse"];
export type Publication = Schemas["PublicationResponse"];
export type Account = Schemas["AccountResponse"];
export type Session = Schemas["SessionResponse"];
export type DeletionBlocker = Schemas["DeletionBlocker"];
export type Member = Schemas["MemberResponse"];
/** An invitation that has been sent and not yet accepted, withdrawn or expired. */
export type Invitation = Schemas["InvitationResponse"];
/** What an invitation is for, read without spending it. */
export type InvitationPreview = Schemas["InvitationPreviewResponse"];
export type NotificationItem = Schemas["NotificationResponse"];
export type NotificationFeed = Schemas["NotificationFeedResponse"];
/** One switch on the settings page: this kind, on this channel. */
export type NotificationPreference = Schemas["NotificationPreferenceState"];
export type Acknowledgement = Schemas["AcknowledgementResponse"];
/** What the API answers a request whose shape it refused: RFC 9457, with `errors` per field. */
export type ValidationProblem = Schemas["HttpValidationProblemDetails"];

/**
 * The names the API serialises a notification's kind and channel as. The schema types both
 * as `string`, so these are the front end's vocabulary for the ones it knows how to show,
 * not the response's shape: an item of a kind added later still arrives and still renders.
 */
export type NotificationKind =
  | "MemberJoined"
  | "MemberLeft"
  | "RunCompleted"
  | "RemovedFromOrganization"
  | "InvitationDeclined";
export type NotificationChannel = "InApp" | "Email";

/** A diagnostic once the dialog has numbered it, which is how the list and the gutter agree. */
export type IdentifiedDiagnostic = SpecDiagnostic & { id: number };

/**
 * Mirrors of the Domain enums. The OpenAPI document types each as a bare `number`, so the
 * members live here, and each one's union type is derived from its object: a label map
 * declared over the union cannot silently miss a member, which `Record<number, string>` let
 * it do.
 */
type MemberOf<T> = T[keyof T];

/** Mirrors SpaceCraft.Domain.Organizations.OrganizationRole. */
export const OrganizationRole = {
  Viewer: 1,
  Member: 2,
  Admin: 3,
  Owner: 4,
} as const;
export type OrganizationRole = MemberOf<typeof OrganizationRole>;

/** Mirrors SpaceCraft.Domain.Pipelines.RunStatus. */
export const RunStatus = {
  Queued: 1,
  Running: 2,
  Succeeded: 3,
  Failed: 4,
  Cancelled: 5,
} as const;
export type RunStatus = MemberOf<typeof RunStatus>;

export const StepStatus = {
  Pending: 1,
  Running: 2,
  Succeeded: 3,
  Failed: 4,
  Skipped: 5,
} as const;
export type StepStatus = MemberOf<typeof StepStatus>;

export const StepKind = {
  Validate: 1,
  Generate: 2,
  Build: 3,
  Publish: 4,
} as const;
export type StepKind = MemberOf<typeof StepKind>;

export const SdkLanguage = {
  TypeScript: 1,
  CSharp: 2,
  Java: 3,
  Python: 4,
  Php: 5,
  Go: 6,
} as const;
export type SdkLanguage = MemberOf<typeof SdkLanguage>;

export const RegistryKind = {
  Npm: 1,
  NuGet: 2,
  Maven: 3,
  PyPI: 4,
  Packagist: 5,
} as const;
export type RegistryKind = MemberOf<typeof RegistryKind>;

/** Mirrors SpaceCraft.Infrastructure.OpenApi.DiagnosticSeverity. */
export const DiagnosticSeverity = {
  Information: 0,
  Warning: 1,
  Error: 2,
} as const;
export type DiagnosticSeverity = MemberOf<typeof DiagnosticSeverity>;

/**
 * Mirrors SpaceCraft.Domain.Registries.PublicationStatus.
 *
 * Failed is 3 and AlreadyExists is 4. A copy of this living in PublicationsPage had the two
 * the other way round, so a failed publication was reported as one the registry already had.
 */
export const PublicationStatus = {
  Pending: 1,
  Succeeded: 2,
  Failed: 3,
  AlreadyExists: 4,
} as const;
export type PublicationStatus = MemberOf<typeof PublicationStatus>;

export const SpecValidationStatus = {
  Pending: 1,
  Valid: 2,
  ValidWithWarnings: 3,
  Invalid: 4,
} as const;
export type SpecValidationStatus = MemberOf<typeof SpecValidationStatus>;

// Indexed by number, since that is what the API sends, and complete over the union, since
// that is what the app knows: a member added to the enum without a label fails to compile
// here rather than rendering as undefined in a badge.
export const runStatusLabel: Record<number, string> = {
  1: "Queued",
  2: "Running",
  3: "Succeeded",
  4: "Failed",
  5: "Cancelled",
} satisfies Record<RunStatus, string>;

export const stepKindLabel: Record<number, string> = {
  1: "Validate",
  2: "Generate",
  3: "Build",
  4: "Publish",
} satisfies Record<StepKind, string>;

export const stepStatusLabel: Record<number, string> = {
  1: "Pending",
  2: "Running",
  3: "Succeeded",
  4: "Failed",
  5: "Skipped",
} satisfies Record<StepStatus, string>;

export const languageLabel: Record<number, string> = {
  1: "TypeScript",
  2: "C#",
  3: "Java",
  4: "Python",
  5: "PHP",
  6: "Go",
} satisfies Record<SdkLanguage, string>;

export const registryLabel: Record<number, string> = {
  1: "npm",
  2: "NuGet",
  3: "Maven",
  4: "PyPI",
  5: "Packagist",
} satisfies Record<RegistryKind, string>;

export const roleLabel: Record<number, string> = {
  1: "Viewer",
  2: "Member",
  3: "Admin",
  4: "Owner",
} satisfies Record<OrganizationRole, string>;

/**
 * Whether a run can be started from this document: anything validation did not refuse. The
 * specs table's filter, the generate dialog's list and the layout's "nothing to generate"
 * state all ask this, and used to spell the comparison out each for themselves.
 */
export function isGeneratable(spec: { validationStatus: number }): boolean {
  return spec.validationStatus !== SpecValidationStatus.Invalid;
}

/** A run in one of these states will not change again; stop polling it. */
export function isTerminal(status: number): boolean {
  return status === RunStatus.Succeeded || status === RunStatus.Failed || status === RunStatus.Cancelled;
}

/**
 * Steps that will not run again, whatever their outcome.
 *
 * Counting only Succeeded understates a finished run: a pipeline that was not asked to
 * publish leaves that step Skipped, so a perfectly good run reported "3/4 steps" and read
 * as though something had not completed.
 */
export function isStepSettled(status: number): boolean {
  return status === StepStatus.Succeeded
    || status === StepStatus.Failed
    || status === StepStatus.Skipped;
}

/** Mirrors SpaceCraft.Domain.Pipelines.RunTrigger. */
export const RunTrigger = {
  Manual: 1,
  GitHubPush: 2,
  SpecUpload: 3,
} as const;
export type RunTrigger = MemberOf<typeof RunTrigger>;
