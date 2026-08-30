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

/** Mirrors SpaceCraft.Infrastructure.OpenApi.DiagnosticSeverity. */
export const DiagnosticSeverity = {
  Information: 0,
  Warning: 1,
  Error: 2,
} as const;

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

/**
 * How long a finished run took, as prose. Once a run is over, "4/4 steps" tells the reader
 * nothing they cannot see from its status — the time it took is the useful number.
 */
/** Mirrors SpaceCraft.Domain.Pipelines.RunTrigger. */
export const RunTrigger = {
  Manual: 1,
  GitHubPush: 2,
  SpecUpload: 3,
} as const;

/** Shared, so two places measuring the same thing cannot word it two ways. */
export function formatSeconds(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));

  if (whole < 60) {
    return `${whole}s`;
  }

  const minutes = Math.floor(whole / 60);
  const rest = whole % 60;
  return rest === 0 ? `${minutes}m` : `${minutes}m ${rest}s`;
}

export function formatRunDuration(run: {
  startedAt: string | null;
  completedAt: string | null;
}): string {
  if (!run.startedAt || !run.completedAt) {
    return "—";
  }

  return formatSeconds(
    (new Date(run.completedAt).getTime() - new Date(run.startedAt).getTime()) / 1000,
  );
}

/**
 * What a run is doing right now, as a phrase.
 *
 * "3/4 steps" says how far along a run is but not what it is waiting on, and the steps are
 * not comparable: on a large spec, generating takes seconds while building takes minutes.
 * Naming the running step is what tells someone whether to wait.
 */
export function describeRunProgress(run: {
  steps: { kind: number; status: number }[];
}): string {
  const running = run.steps.find((s) => s.status === StepStatus.Running);
  const settled = run.steps.filter((s) => isStepSettled(s.status)).length;

  return running
    ? `${stepKindLabel[running.kind]}… ${settled}/${run.steps.length}`
    : `${settled}/${run.steps.length} steps`;
}

