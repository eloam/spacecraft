import { parseDocument } from "yaml";
import { lineAt, lineStarts, offsetForPointer } from "../lib/specPointer";

export type IndexRequest = {
  text: string;
  pointers: (string | null | undefined)[];
};

export type IndexResponse = {
  /** Parallel to the pointers sent in; null where the pointer resolved to nothing. */
  lines: (number | null)[];
  /** Set when the document could not be parsed at all — the viewer still shows the text. */
  error?: string;
};

/**
 * Resolves every finding's pointer to a line, off the main thread.
 *
 * A 400 KB spec with several hundred findings is a parse plus several hundred tree walks.
 * On the main thread that is a visible freeze at the moment the dialog opens, which is the
 * one moment the user is waiting on it.
 *
 * JSON is parsed by the YAML parser too: JSON is a subset of YAML, and one parser means one
 * offset model to reason about rather than two that agree only most of the time.
 */
self.onmessage = (event: MessageEvent<IndexRequest>) => {
  const { text, pointers } = event.data;

  let response: IndexResponse;
  try {
    const doc = parseDocument(text);
    const starts = lineStarts(text);

    response = {
      lines: pointers.map((pointer) => {
        if (!pointer) return null;
        const offset = offsetForPointer(doc, pointer);
        return offset === null ? null : lineAt(starts, offset);
      }),
    };
  } catch (error) {
    // A document that fails to parse is exactly the case with findings worth reading, so
    // this reports no lines rather than no viewer.
    response = {
      lines: pointers.map(() => null),
      error: error instanceof Error ? error.message : "The document could not be parsed.",
    };
  }

  self.postMessage(response);
};
