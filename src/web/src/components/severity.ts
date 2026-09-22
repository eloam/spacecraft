import { CircleAlert, Info, TriangleAlert, type LucideIcon } from "lucide-react";
import { DiagnosticSeverity } from "@/api/types";

/**
 * How a severity is drawn and named, the same wherever findings are counted, filtered or
 * listed: the counts column, the filter strips and the findings list share these, so a
 * filter and what it selects look alike.
 *
 * `noun` takes the plural's "+ s". "Information" has no plural in English, so it cannot;
 * "note" can, it is what the badge on the specs table already says, and it is shorter.
 * `label` is the same word in title case and plural, the way a filter names its set.
 */
export const severities: Record<
  DiagnosticSeverity,
  { noun: string; label: string; Icon: LucideIcon; tone: string }
> = {
  [DiagnosticSeverity.Error]: {
    noun: "error",
    label: "Errors",
    Icon: CircleAlert,
    tone: "text-destructive",
  },
  [DiagnosticSeverity.Warning]: {
    noun: "warning",
    label: "Warnings",
    Icon: TriangleAlert,
    tone: "text-[var(--code-warning)]",
  },
  [DiagnosticSeverity.Information]: {
    noun: "note",
    label: "Notes",
    Icon: Info,
    tone: "text-muted-foreground",
  },
};

/** Worst first: the order every count, strip and list shows the three in. */
export const SEVERITY_ORDER = [
  DiagnosticSeverity.Error,
  DiagnosticSeverity.Warning,
  DiagnosticSeverity.Information,
] as const;

function isSeverity(value: number): value is DiagnosticSeverity {
  return Object.values<number>(DiagnosticSeverity).includes(value);
}

/**
 * The API types a severity as a bare number, so one added on the server before the front end
 * learns it can arrive. It still renders, and still counts, as a note.
 */
export function severityOf(value: number): DiagnosticSeverity {
  return isSeverity(value) ? value : DiagnosticSeverity.Information;
}
