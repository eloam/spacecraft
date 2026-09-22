const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
const date = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

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
    ["second", 60], ["minute", 60], ["hour", 24], ["day", 30], ["month", 12],
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
