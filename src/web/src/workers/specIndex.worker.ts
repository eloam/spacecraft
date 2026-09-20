import { parseDocument } from "yaml";
import { lineAt, lineStarts, offsetForPointer } from "../lib/specPointer";

export type IndexRequest = {
  text: string;
  pointers: (string | null | undefined)[];
};

export type IndexResponse = {
  /** Parallel to the pointers sent in; null where the pointer resolved to nothing. */
  lines: (number | null)[];
  /**
   * Set when the parser reported a syntax error — the lines above are then as far as it got,
   * with a line and column for where it stopped — or when something else threw, in which
   * case there are no lines at all. The viewer shows the text either way.
   */
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
    // A syntax error does not throw: the parser recovers and collects it in `doc.errors`,
    // which is exactly the case with findings worth reading. So the partial tree is walked
    // — a pointer that resolves against it is a real line — and the first error is reported
    // beside the result rather than instead of it.
    const doc = parseDocument(text);
    const starts = lineStarts(text);

    response = {
      lines: pointers.map((pointer) => {
        if (!pointer) return null;
        const offset = offsetForPointer(doc, pointer);
        return offset === null ? null : lineAt(starts, offset);
      }),
    };

    if (doc.errors.length > 0) {
      // The first line of the message is the sentence with its line and column; the parser
      // follows it with a code frame of the offending line, which the dialog reading this
      // already has on screen.
      response.error = doc.errors[0].message.split("\n", 1)[0].replace(/:$/, "");
    }
  } catch (error) {
    // Anything the parser or the walk throws outright, which the recovery above does not
    // cover. No lines, but still an answer: the alternative is a skeleton that never ends.
    response = {
      lines: pointers.map(() => null),
      error: error instanceof Error ? error.message : "The document could not be indexed.",
    };
  }

  self.postMessage(response);
};
