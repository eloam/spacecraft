/**
 * Where one step from `at` lands, in a list of `count` items.
 *
 * Wrapping both ways, so stepping past either end continues instead of stopping on a button
 * that silently does nothing. With nothing selected yet — `at` below zero — forwards means
 * the first item and backwards the last, rather than an index counted from a position that
 * does not exist. Null when there is nothing to step through.
 *
 * One rule for every dialog that walks a document's marks — the changes of a diff, the
 * findings of a validation — because it is the same gesture.
 */
export function wrapStep(at: number, by: 1 | -1, count: number): number | null {
  if (count === 0) return null;
  if (at < 0) return by === 1 ? 0 : count - 1;
  return (at + by + count) % count;
}
