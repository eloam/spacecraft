import { useEffect, useRef } from "react";
import { StateEffect, StateField } from "@codemirror/state";
import { Decoration, EditorView } from "@codemirror/view";
import { MergeView } from "@codemirror/merge";
import { specExtensions } from "@/lib/codemirror";

/**
 * Which change is being looked at, held in the right-hand editor's state.
 *
 * The merge view tints every changed line the same, which answers "what moved" but not "where
 * am I" once you start stepping through them.
 */
const setCurrentChunk = StateEffect.define<{ from: number; to: number } | null>();

const currentChunk = StateField.define<{ from: number; to: number } | null>({
  create: () => null,
  update(value, tr) {
    for (const effect of tr.effects) {
      if (effect.is(setCurrentChunk)) return effect.value;
    }
    return value;
  },
});

const currentChunkHighlight = EditorView.decorations.compute([currentChunk, "doc"], (state) => {
  const chunk = state.field(currentChunk);
  if (!chunk) return Decoration.none;

  const ranges = [];
  const last = state.doc.lineAt(Math.max(chunk.from, Math.min(chunk.to, state.doc.length) - 1));

  for (let line = state.doc.lineAt(chunk.from); ; line = state.doc.line(line.number + 1)) {
    ranges.push(Decoration.line({ class: "sc-current-chunk" }).range(line.from));
    if (line.number >= last.number || line.number >= state.doc.lines) break;
  }

  return Decoration.set(ranges);
});

const theme = EditorView.theme({
  ".cm-merge-revert": { display: "none" },
  // Tinted from the app's own tokens rather than the addon's defaults, which are fixed greens
  // and reds that ignore the theme.
  ".cm-changedLine": { backgroundColor: "color-mix(in oklch, var(--code-warning) 14%, transparent)" },
  ".cm-deletedChunk": { backgroundColor: "color-mix(in oklch, var(--destructive) 10%, transparent)" },
  ".cm-changedText": { backgroundColor: "color-mix(in oklch, var(--code-warning) 26%, transparent)" },
  ".sc-current-chunk": { boxShadow: "inset 2px 0 0 0 var(--foreground)" },
});

/**
 * Two spec versions side by side, with what changed between them.
 *
 * The reason a version history exists is to answer "what changed" — and until now the only
 * way to ask was to read both documents and hold them in your head.
 *
 * Read-only on both sides: these are immutable snapshots, and the merge view's revert arrows
 * would offer to write one into the other.
 */
export function SpecDiffView({
  before,
  after,
  format,
  scrollTo,
  onChunkCount,
}: {
  /** The older document — shown on the left. */
  before: string;
  after: string;
  format: string;
  /**
   * Which change to move to. A counter rides along so asking for the same one twice — next,
   * previous, next again — still scrolls.
   */
  scrollTo: { index: number; nonce: number } | null;
  onChunkCount: (count: number) => void;
}) {
  const host = useRef<HTMLDivElement>(null);
  const merge = useRef<MergeView | null>(null);

  useEffect(() => {
    if (!host.current) return;

    const view = new MergeView({
      a: { doc: before, extensions: [...specExtensions(format), theme] },
      b: {
        doc: after,
        extensions: [...specExtensions(format), theme, currentChunk, currentChunkHighlight],
      },
      parent: host.current,
      // Collapsing the untouched stretches is what makes a 400 KB document comparable at all:
      // without it, two near-identical specs are 11,000 lines of agreement to scroll past.
      collapseUnchanged: { margin: 3, minSize: 6 },
      highlightChanges: true,
      gutter: true,
    });

    merge.current = view;
    onChunkCount(view.chunks.length);

    return () => {
      view.destroy();
      merge.current = null;
    };
  }, [before, after, format, onChunkCount]);

  useEffect(() => {
    const view = merge.current;
    if (!view || !scrollTo) return;

    const chunk = view.chunks[scrollTo.index];
    if (!chunk) return;

    // fromB/toB: the right-hand document is the newer one, and a change is read as what the
    // new version says.
    view.b.dispatch({
      effects: [
        setCurrentChunk.of({ from: chunk.fromB, to: chunk.toB }),
        EditorView.scrollIntoView(chunk.fromB, { y: "center" }),
      ],
    });
  }, [scrollTo]);

  return <div ref={host} className="h-full overflow-auto" />;
}
