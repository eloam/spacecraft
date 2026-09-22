import { useEffect, useEffectEvent, useRef } from "react";
import { EditorState, StateEffect, StateField, type Range } from "@codemirror/state";
import { Decoration, EditorView, WidgetType, gutter, GutterMarker } from "@codemirror/view";
import { DiagnosticSeverity, type IdentifiedDiagnostic } from "@/api/types";
import { specExtensions } from "@/lib/codemirror";

export type LocatedFinding = IdentifiedDiagnostic & { line: number };

const severityClass: Record<number, string> = {
  [DiagnosticSeverity.Error]: "sc-error",
  [DiagnosticSeverity.Warning]: "sc-warning",
  [DiagnosticSeverity.Information]: "sc-info",
};

/** The most serious severity in a group — a line with an error is an error line. */
function worst(findings: IdentifiedDiagnostic[]): number {
  let highest: number = DiagnosticSeverity.Information;
  for (const finding of findings) {
    if (finding.severity > highest) highest = finding.severity;
  }
  return highest;
}

class MessageWidget extends WidgetType {
  readonly findings: IdentifiedDiagnostic[];
  readonly currentId: number | null;
  readonly heading: string | null;

  constructor(
    findings: IdentifiedDiagnostic[],
    currentId: number | null,
    heading: string | null = null,
  ) {
    super();
    this.findings = findings;
    this.currentId = currentId;
    this.heading = heading;
  }

  // Identity comparison is enough: the arrays are rebuilt only when findings change, so an
  // unchanged group keeps its DOM instead of being torn down on every scroll.
  override eq(other: MessageWidget) {
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

  override eq(other: SeverityMarker) {
    return other.severity === this.severity;
  }

  override toDOM() {
    const dot = document.createElement("div");
    dot.className = `sc-dot ${severityClass[this.severity] ?? "sc-info"}`;
    return dot;
  }
}

type ViewState = {
  findings: LocatedFinding[];
  unlocated: IdentifiedDiagnostic[];
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

const theme = EditorView.theme({
  // Fills the host and scrolls inside it; the diff view instead lets the merge addon size its
  // editors to their content and scrolls the host.
  "&": { height: "100%" },
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
 * `scrollTo` is a value rather than a callback, and carries a `nonce` beside the `line` so that
 * asking for the same line twice — next, then previous, then next again — still scrolls: each
 * request is a new value even when the line is not.
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
  unlocated: IdentifiedDiagnostic[];
  currentId: number | null;
  /** line 0 means the block above the first line, where unlocated findings live. */
  scrollTo: { line: number; nonce: number } | null;
}) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);

  // What the editor opens with. An Effect Event rather than a dependency: the editor is
  // rebuilt for a new document, and depending on the findings would rebuild it on every step
  // as well — the very thing findingState exists to avoid.
  const initialViewState = useEffectEvent((): ViewState => ({ findings, unlocated, currentId }));

  useEffect(() => {
    if (!host.current) return;

    const editor = new EditorView({
      state: EditorState.create({
        doc: content,
        extensions: [
          ...specExtensions(format),
          // Seeded at creation, since the effect below only dispatches when the findings
          // change: an editor rebuilt for a new document would otherwise open without them.
          findingState.init(() => initialViewState()),
          decorations,
          severityGutter,
          theme,
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
