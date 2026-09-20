import { isMap, isNode, isScalar, isSeq, type Document } from "yaml";

/**
 * Turns a JSON pointer into its segments.
 *
 * RFC 6901 escaping is undone here: "~1" is a literal "/" inside a key, "~0" a literal "~".
 * Order matters — "~01" must decode to "~1", not to "/", so "~1" is replaced first.
 */
export function decodePointer(pointer: string): string[] {
  let rest = pointer.startsWith("#") ? pointer.slice(1) : pointer;
  if (rest === "" || rest === "/") return [];
  if (rest.startsWith("/")) rest = rest.slice(1);

  return rest.split("/").map((segment) => segment.replace(/~1/g, "/").replace(/~0/g, "~"));
}

function pairFor(node: unknown, key: string) {
  if (!isMap(node)) return null;

  for (const pair of node.items) {
    if (isScalar(pair.key) && String(pair.key.value) === key) return pair;
  }

  return null;
}

function startOffset(node: unknown): number | null {
  return isNode(node) ? (node.range?.[0] ?? null) : null;
}

/**
 * Walks the segments down the document and returns the character offset they land on.
 *
 * Segments are matched longest-first, several at a time, because a key may itself contain a
 * "/" that whoever wrote the pointer failed to escape. Every finding SpaceCraft recorded
 * before the escaping was fixed looks like "#/paths//admin/realms" — three segments that are
 * really one key, "/admin/realms". Joining candidates back together and backtracking on a
 * miss resolves both spellings, so old findings keep working without being rewritten.
 *
 * The offset is the key's, not the value's: an IDE puts the marker on the line where the
 * thing is named, and for a mapping that is the key.
 */
function walk(node: unknown, segments: string[], index: number): number | null {
  if (index >= segments.length) return startOffset(node);

  if (isMap(node)) {
    for (let take = segments.length - index; take >= 1; take--) {
      const pair = pairFor(node, segments.slice(index, index + take).join("/"));
      if (!pair) continue;

      if (index + take >= segments.length) {
        return startOffset(pair.key) ?? startOffset(pair.value);
      }

      const deeper = walk(pair.value, segments, index + take);
      if (deeper !== null) return deeper;
    }

    return null;
  }

  if (isSeq(node)) {
    const at = Number(segments[index]);
    if (!Number.isInteger(at) || at < 0 || at >= node.items.length) return null;

    return walk(node.items[at], segments, index + 1);
  }

  return null;
}

export function offsetForPointer(doc: Document, pointer: string): number | null {
  const segments = decodePointer(pointer);
  if (segments.length === 0) return startOffset(doc.contents);

  return walk(doc.contents, segments, 0);
}

/** Offsets of the first character of every line, for turning an offset into a line number. */
export function lineStarts(text: string): number[] {
  const starts = [0];
  for (let i = 0; i < text.length; i++) {
    if (text.charCodeAt(i) === 10) starts.push(i + 1);
  }
  return starts;
}

/** 1-based line number containing `offset`, by binary search over `starts`. */
export function lineAt(starts: number[], offset: number): number {
  let low = 0;
  let high = starts.length - 1;

  while (low < high) {
    const mid = Math.ceil((low + high) / 2);
    // `mid` is always inside the array; the index type says it may not be, so the fallback
    // only keeps the search honest for the checker.
    if ((starts[mid] ?? Infinity) <= offset) low = mid;
    else high = mid - 1;
  }

  return low + 1;
}
