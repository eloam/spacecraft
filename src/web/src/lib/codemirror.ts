import { EditorState, type Extension } from "@codemirror/state";
import { EditorView, lineNumbers } from "@codemirror/view";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { json } from "@codemirror/lang-json";
import { yaml } from "@codemirror/lang-yaml";
import { tags } from "@lezer/highlight";

/**
 * Deliberately close to the app's greyscale: weight and a little tone carry the structure,
 * so a spec reads as part of SpaceCraft rather than as an editor dropped into it.
 */
export const highlight = HighlightStyle.define([
  {
    tag: [tags.propertyName, tags.definition(tags.propertyName)],
    color: "var(--code-key)",
    fontWeight: "500",
  },
  { tag: [tags.string, tags.special(tags.string)], color: "var(--code-string)" },
  { tag: [tags.number, tags.bool, tags.null, tags.keyword], color: "var(--code-atom)" },
  { tag: tags.comment, color: "var(--muted-foreground)", fontStyle: "italic" },
  { tag: [tags.punctuation, tags.separator], color: "var(--muted-foreground)" },
]);

/** The chrome every spec editor shares; a view adds its own theme for what only it draws. */
export const theme = EditorView.theme({
  "&": { backgroundColor: "transparent", color: "var(--foreground)" },
  ".cm-scroller": {
    fontFamily: "var(--font-mono, ui-monospace, monospace)",
    fontSize: "12px",
    lineHeight: "1.6",
  },
  ".cm-gutters": {
    backgroundColor: "transparent",
    borderRight: "1px solid var(--border)",
    color: "var(--muted-foreground)",
  },
});

/**
 * What every read-only view of a spec is built from: the document's own language, numbered
 * lines, wrapping, and the shared look.
 *
 * Read-only takes two facets because CodeMirror keeps them apart: `readOnly` stops commands
 * from changing the document, `editable` keeps the DOM from being contenteditable, and a
 * viewer wants both.
 */
export function specExtensions(format: string): Extension[] {
  return [
    lineNumbers(),
    format === "json" ? json() : yaml(),
    syntaxHighlighting(highlight),
    theme,
    EditorState.readOnly.of(true),
    EditorView.editable.of(false),
    EditorView.lineWrapping,
  ];
}
