import { useEffect, useRef } from "react";
import { EditorState, StateEffect, StateField, type Range } from "@codemirror/state";
import { Decoration, EditorView, WidgetType, gutter, GutterMarker, lineNumbers } from "@codemirror/view";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { json } from "@codemirror/lang-json";
import { yaml } from "@codemirror/lang-yaml";
import { tags } from "@lezer/highlight";
import { DiagnosticSeverity } from "@/api/types";

export type Finding = {
  id: number;
  severity: number;
  message: string;
};

export type LocatedFinding = Finding & { line: number };

const severityClass: Record<number, string> = {
  [DiagnosticSeverity.Error]: "sc-error",
  [DiagnosticSeverity.Warning]: "sc-warning",
  [DiagnosticSeverity.Information]: "sc-info",
};

/** The most serious severity in a group — a line with an error is an error line. */
function worst(findings: Finding[]): number {
  let highest: number = DiagnosticSeverity.Information;
  for (const finding of findings) {
    if (finding.severity > highest) highest = finding.severity;
  }
  return highest;
}

class MessageWidget extends WidgetType {
  readonly findings: Finding[];
  readonly currentId: number | null;
  readonly heading: string | null;

  constructor(findings: Finding[], currentId: number | null, heading: string | null = null) {
    super();
    this.findings = findings;
    this.currentId = currentId;
    this.heading = heading;
  }

  // Identity comparison is enough: the arrays are rebuilt only when findings change, so an
  // unchanged group keeps its DOM instead of being torn down on every scroll.
  eq(other: MessageWidget) {
    return (
      other.findings === this.findings &&
      other.currentId === this.currentId &&
      other.heading === this.heading
    );
  }

  toDOM() {
    const wrap = document.createElement("div");
    wrap.className = "sc-messages";

    if (this.heading) {
      const label = document.createElement("div");
      label.className = "sc-heading";
      label.textContent = this.heading;
      wrap.append(label);
    }

    for (const finding of this.findings) {
      const row = document.createElement("div");
      row.className = `sc-message ${severityClass[finding.severity] ?? "sc-info"}`;
      if (finding.id === this.currentId) row.classList.add("sc-current");
      row.textContent = finding.message;
      wrap.append(row);
    }

    return wrap;
  }
}

class SeverityMarker extends GutterMarker {
  readonly severity: number;

  constructor(severity: number) {
    super();
    this.severity = severity;
  }

  eq(other: SeverityMarker) {
    return other.severity === this.severity;
  }

  toDOM() {
    const dot = document.createElement("div");
    dot.className = `sc-dot ${severityClass[this.severity] ?? "sc-info"}`;
    return dot;
  }
}

type ViewState = {
  findings: LocatedFinding[];
  unlocated: Finding[];
  currentId: number | null;
};

const setViewState = StateEffect.define<ViewState>();

/**
 * Findings held in editor state rather than in React.
 *
 * Recreating the editor whenever the selection changes would throw away scroll position and
 * re-parse the document — on a 400 KB spec that is the difference between stepping through
 * findings and waiting on each step.
 */
const findingState = StateField.define<ViewState>({
  create: () => ({ findings: [], unlocated: [], currentId: null }),
  update(value, tr) {
    for (const effect of tr.effects) {
      if (effect.is(setViewState)) return effect.value;
    }
    return value;
  },
});

function groupByLine(findings: LocatedFinding[]) {
  const groups = new Map<number, LocatedFinding[]>();
  for (const finding of findings) {
    const existing = groups.get(finding.line);
    if (existing) existing.push(finding);
    else groups.set(finding.line, [finding]);
  }
  return groups;
}

const decorations = EditorView.decorations.compute([findingState, "doc"], (state) => {
  const { findings, unlocated, currentId } = state.field(findingState);
  const ranges: Range<Decoration>[] = [];

  // Findings whose pointer addresses nothing the document contains. They sit above the first
  // line rather than being left out: they are still findings, and the top of the document is
  // the only honest place for something that belongs to no line in particular.
  if (unlocated.length > 0) {
    ranges.push(
      Decoration.widget({
        widget: new MessageWidget(
          unlocated,
          unlocated.some((f) => f.id === currentId) ? currentId : null,
          "Not tied to a line",
        ),
        block: true,
        side: -1,
      }).range(0),
    );
  }

  for (const [line, group] of groupByLine(findings)) {
    if (line < 1 || line > state.doc.lines) continue;

    const at = state.doc.line(line);
    const carriesCurrent = group.some((f) => f.id === currentId);

    ranges.push(
      Decoration.line({
        class: `sc-line ${severityClass[worst(group)] ?? "sc-info"}${carriesCurrent ? " sc-current" : ""}`,
      }).range(at.from),
    );

    // A block widget under the line, the way an IDE writes the message into the flow rather
    // than hiding it behind a hover.
    ranges.push(
      Decoration.widget({
        widget: new MessageWidget(group, carriesCurrent ? currentId : null),
        block: true,
        side: 1,
      }).range(at.to),
    );
  }

  return Decoration.set(ranges, true);
});

const severityGutter = gutter({
  class: "sc-gutter",
  lineMarker(view, line) {
    const { findings } = view.state.field(findingState);
    const at = view.state.doc.lineAt(line.from).number;
    const group = findings.filter((f) => f.line === at);

    return group.length > 0 ? new SeverityMarker(worst(group)) : null;
  },
  lineMarkerChange: (update) => update.transactions.some((tr) => tr.effects.some((e) => e.is(setViewState))),
});

/**
 * Deliberately close to the app's greyscale: weight and a little tone carry the structure,
 * so a spec reads as part of SpaceCraft rather than as an editor dropped into it.
 */
const highlight = HighlightStyle.define([
  { tag: [tags.propertyName, tags.definition(tags.propertyName)], color: "var(--code-key)", fontWeight: "500" },
  { tag: [tags.string, tags.special(tags.string)], color: "var(--code-string)" },
  { tag: [tags.number, tags.bool, tags.null, tags.keyword], color: "var(--code-atom)" },
  { tag: tags.comment, color: "var(--muted-foreground)", fontStyle: "italic" },
  { tag: [tags.punctuation, tags.separator], color: "var(--muted-foreground)" },
]);

const theme = EditorView.theme({
  "&": { height: "100%", backgroundColor: "transparent", color: "var(--foreground)" },
  ".cm-scroller": { fontFamily: "var(--font-mono, ui-monospace, monospace)", fontSize: "12px", lineHeight: "1.6" },
  ".cm-gutters": { backgroundColor: "transparent", borderRight: "1px solid var(--border)", color: "var(--muted-foreground)" },
  ".cm-activeLineGutter": { backgroundColor: "transparent" },
  ".cm-content": { caretColor: "transparent" },
  ".cm-cursor": { display: "none" },
  ".sc-gutter": { width: "14px" },
  ".sc-dot": { width: "6px", height: "6px", borderRadius: "9999px", margin: "7px auto", backgroundColor: "currentColor" },
  ".sc-error": { color: "var(--destructive)" },
  ".sc-warning": { color: "var(--code-warning)" },
  ".sc-info": { color: "var(--muted-foreground)" },
  ".sc-line": { backgroundColor: "color-mix(in oklch, currentColor 8%, transparent)" },
  ".sc-line.sc-current": { backgroundColor: "color-mix(in oklch, currentColor 20%, transparent)" },
  ".sc-messages": { padding: "0 0 2px 0" },
  ".sc-heading": {
    padding: "4px 8px 2px",
    fontSize: "10px",
    letterSpacing: "0.04em",
    textTransform: "uppercase",
    color: "var(--muted-foreground)",
  },
  ".sc-message": {
    padding: "1px 8px 3px",
    fontSize: "11px",
    borderLeft: "2px solid currentColor",
    backgroundColor: "color-mix(in oklch, currentColor 7%, transparent)",
  },
  ".sc-message.sc-current": { backgroundColor: "color-mix(in oklch, currentColor 16%, transparent)" },
});

/**
 * Read-only view of the uploaded document with findings marked against their lines.
 *
 * `scrollToLine` is a value rather than a callback so that asking for the same line twice —
 * next, then previous, then next again — still scrolls: the effect keys on the counter that
 * comes with it, not on the line alone.
 */
export function SpecSourceView({
  content,
  format,
  findings,
  unlocated,
  currentId,
  scrollTo,
}: {
  content: string;
  format: string;
  findings: LocatedFinding[];
  unlocated: Finding[];
  currentId: number | null;
  /** line 0 means the block above the first line, where unlocated findings live. */
  scrollTo: { line: number; nonce: number } | null;
}) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);

  useEffect(() => {
    if (!host.current) return;

    const editor = new EditorView({
      state: EditorState.create({
        doc: content,
        extensions: [
          lineNumbers(),
          format === "json" ? json() : yaml(),
          syntaxHighlighting(highlight),
          findingState,
          decorations,
          severityGutter,
          theme,
          EditorState.readOnly.of(true),
          EditorView.editable.of(false),
          EditorView.lineWrapping,
        ],
      }),
      parent: host.current,
    });

    view.current = editor;
    return () => {
      editor.destroy();
      view.current = null;
    };
  }, [content, format]);

  useEffect(() => {
    view.current?.dispatch({ effects: setViewState.of({ findings, unlocated, currentId }) });
  }, [findings, unlocated, currentId]);

  useEffect(() => {
    const editor = view.current;
    if (!editor || !scrollTo) return;
    if (scrollTo.line < 0 || scrollTo.line > editor.state.doc.lines) return;

    // Line 0 is the unlocated block; it sits at offset 0, and "start" rather than "center"
    // keeps it from being scrolled half out of view at the top of the document.
    const to = scrollTo.line === 0 ? 0 : editor.state.doc.line(scrollTo.line).from;
    editor.dispatch({
      effects: EditorView.scrollIntoView(to, { y: scrollTo.line === 0 ? "start" : "center" }),
    });
  }, [scrollTo]);

  return <div ref={host} className="h-full overflow-hidden" />;
}
