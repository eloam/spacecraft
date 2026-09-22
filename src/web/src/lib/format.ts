import { StepStatus, isStepSettled, stepKindLabel } from "@/api/types";

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
const date = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const duration = new Intl.DurationFormat(undefined, { style: "narrow" });
// "auto", the default, leaves out every unit that is zero — all of them, for a zero duration.
const zeroDuration = new Intl.DurationFormat(undefined, {
  style: "narrow",
  secondsDisplay: "always",
});
// The copy is English, so its nouns pluralise by English rules whatever the reader's locale.
const english = new Intl.PluralRules("en");

/** An instant, in the reader's locale, to the minute: "20 Sept 2026, 14:03". */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}

/** A day, in the reader's locale: "20 Sept 2026". */
export function formatDate(iso: string): string {
  return date.format(new Date(iso));
}

/**
 * "2 days ago" rather than a timestamp: the columns this appears in are scanned for recency,
 * not read for a precise moment. `RelativeTime` keeps the exact value a focus away for when
 * it is.
 *
 * The formatters are built once: this runs for every row of a table that re-renders on
 * every poll, and `Intl` objects are not cheap to make.
 */
export function timeAgo(iso: string): string {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ["second", 60],
    ["minute", 60],
    ["hour", 24],
    ["day", 30],
    ["month", 12],
  ];

  let value = seconds;
  for (const [unit, size] of units) {
    if (Math.abs(value) < size) {
      return relative.format(-value, unit);
    }
    value = Math.round(value / size);
  }

  // Past a year, "2 years ago" says less than the date: the way GitHub's relative-time
  // element switches to the absolute form after a threshold.
  return formatDate(iso);
}

/**
 * A span of seconds as "1h 5m 12s", in the reader's locale.
 *
 * Whole seconds: the API measures steps to the millisecond and nobody reads that part.
 * Zero is "0s" rather than nothing, so a step that finished within the second still reads as
 * finished.
 */
export function formatSeconds(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  if (whole === 0) return zeroDuration.format({ seconds: 0 });

  return duration.format({
    hours: Math.floor(whole / 3600),
    minutes: Math.floor((whole % 3600) / 60),
    seconds: whole % 60,
  });
}

/** The time between two instants, as `formatSeconds` words it. */
export function formatElapsed(fromIso: string, toIso: string): string {
  return formatSeconds((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 1000);
}

/**
 * How long a finished run took, as prose. Once a run is over, "4/4 steps" tells the reader
 * nothing they cannot see from its status — the time it took is the useful number.
 */
export function formatRunDuration(run: {
  startedAt: string | null;
  completedAt: string | null;
}): string {
  return run.startedAt && run.completedAt ? formatElapsed(run.startedAt, run.completedAt) : "—";
}

/**
 * What a run is doing right now, as a phrase.
 *
 * "3/4 steps" says how far along a run is but not what it is waiting on, and the steps are
 * not comparable: on a large spec, generating takes seconds while building takes minutes.
 * Naming the running step is what tells someone whether to wait.
 */
export function describeRunProgress(run: { steps: { kind: number; status: number }[] }): string {
  const running = run.steps.find((s) => s.status === StepStatus.Running);
  const settled = run.steps.filter((s) => isStepSettled(s.status)).length;

  return running
    ? `${stepKindLabel[running.kind]}… ${settled}/${run.steps.length}`
    : `${settled}/${run.steps.length} steps`;
}

/**
 * "1 project", "2 projects": the count and its noun, for the regular nouns the copy uses.
 * Written three ways before — `=== 1 ? "" : "s"`, `> 1 ? "s" : ""` and "project(s)" — and
 * the second of those made zero singular.
 */
export function plural(count: number, noun: string): string {
  return `${count} ${noun}${english.select(count) === "one" ? "" : "s"}`;
}
