/**
 * "2 days ago" rather than a timestamp: the columns this appears in are scanned for recency,
 * not read for a precise moment. Keep the exact value on a title attribute for when it is.
 */
export function timeAgo(iso: string) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ["second", 60], ["minute", 60], ["hour", 24], ["day", 30], ["month", 12], ["year", Infinity],
  ];

  let value = seconds;
  for (const [unit, size] of units) {
    if (Math.abs(value) < size) {
      return new Intl.RelativeTimeFormat(undefined, { numeric: "auto" }).format(-value, unit);
    }
    value = Math.round(value / size);
  }
  return new Date(iso).toLocaleDateString();
}
