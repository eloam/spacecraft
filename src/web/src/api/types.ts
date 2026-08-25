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
export type SpecUpload = Schemas["SpecUploadResponse"];
export type SpecDiagnostic = Schemas["SpecDiagnostic"];
export type Run = Schemas["RunResponse"];
export type RunStep = Schemas["StepResponse"];
export type Credential = Schemas["CredentialResponse"];

/** Mirrors SpaceCraft.Domain.Organizations.OrganizationRole. */
export const OrganizationRole = {
  Viewer: 1,
  Member: 2,
  Admin: 3,
  Owner: 4,
} as const;

/** Mirrors SpaceCraft.Domain.Pipelines.RunStatus. */
export const RunStatus = {
  Queued: 1,
  Running: 2,
  Succeeded: 3,
  Failed: 4,
  Cancelled: 5,
} as const;

export const StepStatus = {
  Pending: 1,
  Running: 2,
  Succeeded: 3,
  Failed: 4,
  Skipped: 5,
} as const;

export const StepKind = {
  Validate: 1,
  Generate: 2,
  Build: 3,
  Publish: 4,
} as const;

export const SdkLanguage = {
  TypeScript: 1,
  CSharp: 2,
  Java: 3,
  Python: 4,
  Php: 5,
  Go: 6,
} as const;

export const RegistryKind = {
  Npm: 1,
  NuGet: 2,
  Maven: 3,
  PyPI: 4,
  Packagist: 5,
} as const;

export const SpecValidationStatus = {
  Pending: 1,
  Valid: 2,
  ValidWithWarnings: 3,
  Invalid: 4,
} as const;

export const runStatusLabel: Record<number, string> = {
  1: "Queued",
  2: "Running",
  3: "Succeeded",
  4: "Failed",
  5: "Cancelled",
};

export const stepKindLabel: Record<number, string> = {
  1: "Validate",
  2: "Generate",
  3: "Build",
  4: "Publish",
};

export const stepStatusLabel: Record<number, string> = {
  1: "Pending",
  2: "Running",
  3: "Succeeded",
  4: "Failed",
  5: "Skipped",
};

export const languageLabel: Record<number, string> = {
  1: "TypeScript",
  2: "C#",
  3: "Java",
  4: "Python",
  5: "PHP",
  6: "Go",
};

export const registryLabel: Record<number, string> = {
  1: "npm",
  2: "NuGet",
  3: "Maven",
  4: "PyPI",
  5: "Packagist",
};

export const roleLabel: Record<number, string> = {
  1: "Viewer",
  2: "Member",
  3: "Admin",
  4: "Owner",
};

/** A run in one of these states will not change again; stop polling it. */
export function isTerminal(status: number): boolean {
  return status === RunStatus.Succeeded || status === RunStatus.Failed || status === RunStatus.Cancelled;
}

export type ArtifactDownload = Schemas["ArtifactDownloadResponse"];
