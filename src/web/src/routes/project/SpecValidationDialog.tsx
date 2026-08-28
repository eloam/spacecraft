import { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ChevronDown, ChevronUp, CircleAlert, Info, List, RefreshCw, ShieldCheck, ShieldOff,
  SquareCode, TriangleAlert, X,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import type { LocatedFinding, Finding as SourceFinding } from "@/components/SpecSourceView";
import { cn } from "@/lib/utils";
import { useProject, useRevalidateSpec, useSpecContent, useSpecDiagnostics } from "@/api/queries";
import { DiagnosticSeverity } from "@/api/types";
import type { IndexResponse } from "@/workers/specIndex.worker";

/**
 * The editor is a third of the application's JavaScript and is reached from one dialog, so it
 * is fetched when that dialog first needs it rather than by everyone who loads the app.
 */
const SpecSourceView = lazy(() =>
  import("@/components/SpecSourceView").then((m) => ({ default: m.SpecSourceView })),
);

type Finding = {
  id: number;
  severity: number;
  message: string;
  jsonPointer?: string | null;
  line: number | null;
};

const ORDER = [DiagnosticSeverity.Error, DiagnosticSeverity.Warning, DiagnosticSeverity.Information];

/**
 * "Information" has no plural in English, so it cannot take the same "+ s" as the others.
 * "Note" can, it is what the badge on the specs table already says, and it is shorter.
 */
const nouns: Record<number, string> = {
  [DiagnosticSeverity.Error]: "error",
  [DiagnosticSeverity.Warning]: "warning",
  [DiagnosticSeverity.Information]: "note",
};

function count(n: number, noun: string) {
  return `${n} ${noun}${n === 1 ? "" : "s"}`;
}

const icons: Record<number, typeof Info> = {
  [DiagnosticSeverity.Error]: CircleAlert,
  [DiagnosticSeverity.Warning]: TriangleAlert,
  [DiagnosticSeverity.Information]: Info,
};

const tones: Record<number, string> = {
  [DiagnosticSeverity.Error]: "text-destructive",
  [DiagnosticSeverity.Warning]: "text-[var(--code-warning)]",
  [DiagnosticSeverity.Information]: "text-muted-foreground",
};

/**
 * Resolves every finding's pointer to a line in a worker, once per document.
 *
 * Returns null while it is working so callers can tell "not indexed yet" from "indexed, and
 * this finding has no line" — the second is a normal outcome for a finding whose pointer
 * addresses something the document does not contain.
 */
function useFindingLines(text: string | undefined, pointers: (string | null | undefined)[]) {
  const [indexed, setIndexed] = useState<{ text: string; lines: (number | null)[] } | null>(null);

  useEffect(() => {
    if (text === undefined) return;

    const worker = new Worker(new URL("../../workers/specIndex.worker.ts", import.meta.url), {
      type: "module",
    });

    worker.onmessage = (event: MessageEvent<IndexResponse>) => {
      setIndexed({ text, lines: event.data.lines });
      worker.terminate();
    };

    worker.postMessage({ text, pointers });

    return () => worker.terminate();
  }, [text, pointers]);

  // Carrying the document it was computed for means "not indexed yet" is derived from the
  // state rather than written into it when the document changes.
  return indexed !== null && indexed.text === text ? indexed.lines : null;
}

/** Ragged lines, so the wait reads as a document arriving rather than as a stalled panel. */
function SourceSkeleton() {
  return (
    <div className="space-y-2 p-4">
      {Array.from({ length: 14 }, (_, i) => (
        <Skeleton key={i} className="h-3" style={{ width: `${30 + ((i * 23) % 60)}%` }} />
      ))}
    </div>
  );
}

export function SpecValidationDialog({
  orgId,
  projectId,
  specId,
  onClose,
}: {
  orgId: string;
  projectId: string;
  specId: string | null;
  onClose: () => void;
}) {
  const diagnostics = useSpecDiagnostics(orgId, projectId, specId);
  const source = useSpecContent(orgId, projectId, specId);
  const revalidate = useRevalidateSpec(orgId, projectId);
  // Which rules the replay will run under. The server reads it from the project either way;
  // this is here so the button can say so before it is pressed rather than after.
  const strict = useProject(orgId, projectId).data?.strictValidation ?? true;


  const [mode, setMode] = useState<"source" | "list">("source");
  const [hidden, setHidden] = useState<number[]>([]);
  const [currentId, setCurrentId] = useState<number | null>(null);
  const [scrollTo, setScrollTo] = useState<{ line: number; nonce: number } | null>(null);
  const nonce = useRef(0);
  const list = useRef<HTMLUListElement>(null);

  /**
   * Replays validation over the stored document. Without an argument it runs the project's
   * own mode; the menu passes the other one for a single run.
   */
  const run = useCallback(
    (override?: boolean) => {
      if (specId === null) return;

      revalidate.mutate(
        { specId, strict: override },
        {
          onSuccess: (result) => {
            setCurrentId(null);
            // The mode the server reports, not the one that was asked for: an override the
            // API does not honour would otherwise be announced as though it had been.
            toast.success(
              `${count(result.diagnostics.length, "finding")}, revalidated `
              + `${result.strict ? "in strict mode" : "without strict mode"}.`,
            );
          },
          onError: () => toast.error("The document could not be revalidated."),
        },
      );
    },
    [specId, revalidate],
  );

  const pointers = useMemo(
    () => (diagnostics.data ?? []).map((d) => d.jsonPointer),
    [diagnostics.data],
  );
  const lines = useFindingLines(source.data?.content, pointers);

  /**
   * Findings in document order, which is the order "next" should walk. Anything the pointer
   * could not place goes last rather than being dropped: an unplaceable finding is still a
   * finding, and silently losing it would make the counts lie.
   */
  const findings = useMemo<Finding[]>(() => {
    const raw = diagnostics.data ?? [];
    return raw
      .map((d, i) => ({
        id: i,
        severity: d.severity,
        message: d.message,
        jsonPointer: d.jsonPointer,
        line: lines?.[i] ?? null,
      }))
      .sort((a, b) => (a.line ?? Infinity) - (b.line ?? Infinity) || a.id - b.id);
  }, [diagnostics.data, lines]);

  const counts = useMemo(() => {
    const tally: Record<number, number> = { 0: 0, 1: 0, 2: 0 };
    for (const f of findings) tally[f.severity] = (tally[f.severity] ?? 0) + 1;
    return tally;
  }, [findings]);

  const visible = useMemo(
    () => findings.filter((f) => !hidden.includes(f.severity)),
    [findings, hidden],
  );

  const located = useMemo(
    () => visible.filter((f): f is Finding & { line: number } => f.line !== null),
    [visible],
  );

  const unlocated = useMemo(() => visible.filter((f) => f.line === null), [visible]);

  const position = visible.findIndex((f) => f.id === currentId);

  const go = useCallback((to: number) => {
    const next = visible[to];
    if (!next) return;

    setCurrentId(next.id);
    nonce.current += 1;
    // null means it is in the block above the first line, which the view reaches as line 0.
    setScrollTo({ line: next.line ?? 0, nonce: nonce.current });
  }, [visible]);

  const step = useCallback((by: 1 | -1) => {
    if (visible.length === 0) return;

    const from = visible.findIndex((f) => f.id === currentId);
    // Nothing selected yet: forwards means the first finding, backwards the last, rather
    // than an index counted from a position that does not exist.
    if (from < 0) return go(by === 1 ? 0 : visible.length - 1);

    // Wrapping both ways, so stepping past either end continues instead of stopping on a
    // button that silently does nothing.
    go((from + by + visible.length) % visible.length);
  }, [visible, currentId, go]);

  function openInSource(finding: Finding) {
    setMode("source");
    setCurrentId(finding.id);
    nonce.current += 1;
    setScrollTo({ line: finding.line ?? 0, nonce: nonce.current });
  }

  useEffect(() => {
    if (specId === null) return;

    function onKey(event: KeyboardEvent) {
      if (event.key !== "F8") return;
      event.preventDefault();
      step(event.shiftKey ? -1 : 1);
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [specId, step]);

  // Switching to the list from finding 250 of 413 would otherwise land at the top, leaving
  // the reader to find again what they were just looking at.
  useEffect(() => {
    if (mode !== "list") return;
    list.current
      ?.querySelector('[data-current="true"]')
      ?.scrollIntoView({ block: "center" });
  }, [mode, currentId]);

  function close() {
    setMode("source");
    setHidden([]);
    setCurrentId(null);
    setScrollTo(null);
    onClose();
  }

  const loading = diagnostics.isPending || source.isPending || lines === null;

  return (
    <Dialog open={specId !== null} onOpenChange={(open) => !open && close()}>
      <DialogContent
        className="flex h-[92vh] w-[min(1600px,96vw)] max-w-none flex-col gap-0 overflow-hidden p-0 sm:max-w-none"
        showCloseButton={false}
      >
        <div className="flex flex-wrap items-center gap-2 border-b bg-topbar px-3 py-2">
          <DialogTitle className="mr-1 text-sm font-semibold">Validation</DialogTitle>

          {ORDER.map((severity) => {
            const Icon = icons[severity];
            const off = hidden.includes(severity);

            return (
              <Button
                key={severity}
                size="xs"
                variant={off ? "ghost" : "secondary"}
                aria-pressed={!off}
                className={cn("gap-1", off && "opacity-60")}
                onClick={() =>
                  setHidden((prev) =>
                    prev.includes(severity)
                      ? prev.filter((s) => s !== severity)
                      : [...prev, severity],
                  )
                }
              >
                <Icon className={cn("size-3.5", tones[severity])} aria-hidden />
                {count(counts[severity] ?? 0, nouns[severity])}
              </Button>
            );
          })}

          <div className="ml-auto flex items-center gap-1">
            {/* The total until one is picked: "0 / 413" would claim a position in a list
                nobody has stepped into yet. */}
            <span className="mr-1 text-xs tabular-nums text-muted-foreground">
              {visible.length === 0
                ? "No findings"
                : position < 0
                  ? count(visible.length, "finding")
                  : `${position + 1} / ${visible.length}`}
            </span>
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label="Previous finding"
              disabled={visible.length === 0}
              title="Previous finding (Shift+F8)"
              onClick={() => step(-1)}
            >
              <ChevronUp className="size-4" aria-hidden />
            </Button>
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label="Next finding"
              disabled={visible.length === 0}
              title="Next finding (F8)"
              onClick={() => step(1)}
            >
              <ChevronDown className="size-4" aria-hidden />
            </Button>

            <Button
              size="xs"
              variant="outline"
              className="ml-2 gap-1.5"
              onClick={() => setMode(mode === "source" ? "list" : "source")}
            >
              {mode === "source" ? (
                <>
                  <List className="size-3.5" aria-hidden />
                  List
                </>
              ) : (
                <>
                  <SquareCode className="size-3.5" aria-hidden />
                  Source
                </>
              )}
            </Button>

            {/* Findings are written once, at upload, so a spec keeps whatever the rules said
                that day. This replays today's rules over the same document.

                A split control: the button runs the project's own mode, which is what is
                wanted nearly every time, and the chevron opens the one-off — "what would the
                other mode say about this?" — without making it as easy to reach by accident. */}
            <div className="flex items-center">
              <Button
                size="xs"
                variant="outline"
                className="gap-1.5 rounded-r-none border-r-0"
                disabled={specId === null || revalidate.isPending}
                title="Re-run validation over this document"
                onClick={() => run()}
              >
                <RefreshCw
                  className={cn("size-3.5", revalidate.isPending && "animate-spin")}
                  aria-hidden
                />
                Revalidate
              </Button>

              <DropdownMenu>
                <DropdownMenuTrigger
                  render={<Button size="xs" variant="outline" className="rounded-l-none px-1.5" />}
                  disabled={specId === null || revalidate.isPending}
                  aria-label="Revalidate in the other mode"
                >
                  <ChevronDown className="size-3.5 opacity-70" aria-hidden />
                </DropdownMenuTrigger>

                <DropdownMenuContent align="end" className="w-80">
                  {/* Base UI requires a label to sit inside a group: its GroupLabel
                      reads context the group provides, and rendering it loose throws
                      when the menu opens. */}
                  <DropdownMenuGroup>
                    <DropdownMenuLabel className="font-normal">
                      This project validates {strict ? "in strict mode" : "without strict mode"}.
                      Run it the other way once, without changing the setting.
                    </DropdownMenuLabel>
                    <DropdownMenuItem
                      // The item is two lines tall; the icon belongs beside the first of them.
                      className="items-start [&_svg]:mt-0.5"
                      onClick={() => run(!strict)}
                    >
                      {strict ? (
                        <ShieldOff className="size-4 text-muted-foreground" aria-hidden />
                      ) : (
                        <ShieldCheck className="size-4 text-[var(--success)]" aria-hidden />
                      )}
                      <span>
                        {strict ? "Revalidate without strict mode" : "Revalidate in strict mode"}
                        <span className="block text-xs text-muted-foreground">
                          {strict
                            ? "Checks that this is valid OpenAPI, and nothing more. Replaces the findings below."
                            : "Also checks that a usable SDK can come out of this document. Replaces the findings below."}
                        </span>
                      </span>
                    </DropdownMenuItem>
                  </DropdownMenuGroup>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>

            {/* An icon, like the one every other dialog closes with: the word sat in a row
                of icon buttons and read as one more tool rather than the way out. */}
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label="Close"
              title="Close"
              onClick={close}
            >
              <X className="size-4" aria-hidden />
            </Button>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-hidden">
          {loading ? (
            <SourceSkeleton />
          ) : source.isError ? (
            <p className="p-6 text-sm text-muted-foreground">
              The document could not be loaded, so findings cannot be shown against it. The
              list still works.
            </p>
          ) : mode === "source" && source.data ? (
            <Suspense fallback={<SourceSkeleton />}>
              <SpecSourceView
                content={source.data.content}
                format={source.data.format}
                findings={located as LocatedFinding[]}
                unlocated={unlocated as SourceFinding[]}
                currentId={currentId}
                scrollTo={scrollTo}
              />
            </Suspense>
          ) : (
            <ul ref={list} className="h-full divide-y overflow-y-auto">
              {visible.map((finding) => {
                const Icon = icons[finding.severity];

                return (
                  <li
                    key={finding.id}
                    data-current={finding.id === currentId}
                    className={cn(
                      "flex items-start gap-3 px-4 py-2.5 text-sm",
                      finding.id === currentId && "bg-muted/60",
                    )}
                  >
                    <Icon
                      className={cn("mt-0.5 size-4 shrink-0", tones[finding.severity])}
                      aria-hidden
                    />
                    <div className="min-w-0 flex-1">
                      <p>{finding.message}</p>
                      {finding.jsonPointer && (
                        <p className="mt-0.5 font-mono text-xs break-all text-muted-foreground">
                          {finding.jsonPointer}
                        </p>
                      )}
                    </div>

                    {/* Unlocated findings have somewhere to go now — the block above the
                        first line — so they get the same button, labelled for where it
                        lands rather than with a line number it does not have. */}
                    <Button
                      size="xs"
                      variant="ghost"
                      className={cn("shrink-0 gap-1", finding.line !== null && "font-mono")}
                      onClick={() => openInSource(finding)}
                    >
                      <SquareCode className="size-3.5" aria-hidden />
                      {finding.line ?? "Top"}
                    </Button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
