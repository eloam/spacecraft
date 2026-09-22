import { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ChevronDown, ChevronUp, CircleAlert, Info, List, RefreshCw, ShieldCheck, ShieldOff,
  SquareCode, TriangleAlert, X,
} from "lucide-react";
import { toast } from "sonner";
import { Alert, AlertAction, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ButtonGroup } from "@/components/ui/button-group";
import {
  Item, ItemActions, ItemContent, ItemDescription, ItemMedia, ItemTitle,
} from "@/components/ui/item";
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
import { cn } from "@/lib/utils";
import { describeError } from "@/api/client";
import { useProject, useRevalidateSpec, useSpecContent, useSpecDiagnostics } from "@/api/queries";
import { DiagnosticSeverity, type IdentifiedDiagnostic } from "@/api/types";
import type { IndexResponse } from "@/workers/specIndex.worker";

/**
 * The editor is a third of the application's JavaScript and is reached from one dialog, so it
 * is fetched when that dialog first needs it rather than by everyone who loads the app.
 */
const SpecSourceView = lazy(() =>
  import("@/components/SpecSourceView").then((m) => ({ default: m.SpecSourceView })),
);

/**
 * A diagnostic as the dialog holds it: numbered, its severity resolved to one the app knows,
 * and placed — `line` is null while the document is not yet indexed, or when the pointer
 * addresses nothing it contains.
 */
type Finding = IdentifiedDiagnostic & { severity: DiagnosticSeverity; line: number | null };

function isSeverity(value: number): value is DiagnosticSeverity {
  return Object.values<number>(DiagnosticSeverity).includes(value);
}

/**
 * The API types a severity as a bare number, so one added on the server before the front end
 * learns it can arrive. It still renders, and still counts, as a note.
 */
function severityOf(value: number): DiagnosticSeverity {
  return isSeverity(value) ? value : DiagnosticSeverity.Information;
}

const ORDER = [DiagnosticSeverity.Error, DiagnosticSeverity.Warning, DiagnosticSeverity.Information];

/**
 * "Information" has no plural in English, so it cannot take the same "+ s" as the others.
 * "Note" can, it is what the badge on the specs table already says, and it is shorter.
 */
const nouns: Record<DiagnosticSeverity, string> = {
  [DiagnosticSeverity.Error]: "error",
  [DiagnosticSeverity.Warning]: "warning",
  [DiagnosticSeverity.Information]: "note",
};

function count(n: number, noun: string) {
  return `${n} ${noun}${n === 1 ? "" : "s"}`;
}

/** Title case and plural, matching the filter set on the specs table this opens from. */
const labels: Record<DiagnosticSeverity, string> = {
  [DiagnosticSeverity.Error]: "Errors",
  [DiagnosticSeverity.Warning]: "Warnings",
  [DiagnosticSeverity.Information]: "Notes",
};

const icons: Record<DiagnosticSeverity, typeof Info> = {
  [DiagnosticSeverity.Error]: CircleAlert,
  [DiagnosticSeverity.Warning]: TriangleAlert,
  [DiagnosticSeverity.Information]: Info,
};

const tones: Record<DiagnosticSeverity, string> = {
  [DiagnosticSeverity.Error]: "text-destructive",
  [DiagnosticSeverity.Warning]: "text-[var(--code-warning)]",
  [DiagnosticSeverity.Information]: "text-muted-foreground",
};

/**
 * Resolves every finding's pointer to a line in a worker, once per document.
 *
 * `lines` is null while it is working so callers can tell "not indexed yet" from "indexed,
 * and this finding has no line" — the second is a normal outcome for a finding whose pointer
 * addresses something the document does not contain. `error` is what cut the indexing short:
 * the worker's own report about a document it could not fully parse, or the reason it never
 * answered. Either way the lines it did find are real, and the rest go unplaced.
 */
function useFindingLines(text: string | undefined, pointers: (string | null | undefined)[]) {
  const [indexed, setIndexed] = useState<
    { text: string; key: string; lines: (number | null)[]; error: string | null } | null
  >(null);

  // The document alone did not identify the answer. Revalidating leaves the text untouched
  // and replaces the findings, so a result computed for the previous set was still considered
  // current — and the new findings were drawn against the old ones' line numbers, pointing at
  // whatever happened to sit there.
  const key = pointers.join("\u0000");

  useEffect(() => {
    if (text === undefined) return;

    const worker = new Worker(new URL("../../workers/specIndex.worker.ts", import.meta.url), {
      type: "module",
    });

    // One outcome for the three ways a worker can come back. Only `message` used to be
    // wired, so a worker script that failed to load — a chunk gone after a deploy — never
    // settled this state, and the skeleton it holds up stayed up for good.
    const settle = (lines: (number | null)[], error: string | null) => {
      setIndexed({ text, key, lines, error });
      worker.terminate();
    };

    worker.onmessage = (event: MessageEvent<IndexResponse>) =>
      settle(event.data.lines, event.data.error ?? null);
    // An uncaught throw inside the worker, or a script that never loaded. The message can be
    // empty when the browser withholds the detail, and an empty banner explains nothing.
    worker.onerror = (event) =>
      settle(pointers.map(() => null), event.message || "The indexer could not be started.");
    // Deserialising the answer failed on this side: nothing usable crossed.
    worker.onmessageerror = () =>
      settle(pointers.map(() => null), "The indexer's answer could not be read.");

    worker.postMessage({ text, pointers });

    return () => worker.terminate();
  }, [text, pointers, key]);

  // Carrying the document it was computed for means "not indexed yet" is derived from the
  // state rather than written into it when the document changes.
  const current =
    indexed !== null && indexed.text === text && indexed.key === key ? indexed : null;

  return { lines: current?.lines ?? null, error: current?.error ?? null };
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
  spec,
  totals,
  onClose,
}: {
  orgId: string;
  projectId: string;
  specId: string | null;
  /** Which version this is, from the row that opened it. The dialog holds only an id. */
  spec?: { sequence: number; title: string | null };
  /**
   * What the specs row that opened this dialog already knows the document contains. Stands
   * in whenever the findings themselves are not in hand — while they load, and if they
   * fail: tallying a list that never arrived would print "0 errors" about a document nobody
   * managed to check.
   */
  totals?: { errors: number; warnings: number; information: number };
  onClose: () => void;
}) {
  const diagnostics = useSpecDiagnostics(orgId, projectId, specId);
  const source = useSpecContent(orgId, projectId, specId);
  const revalidate = useRevalidateSpec(orgId, projectId);
  // Which rules the replay will run under. The server reads it from the project either way;
  // this is here so the button can say so before it is pressed rather than after.
  const strict = useProject(orgId, projectId).data?.strictValidation ?? true;


  const [mode, setMode] = useState<"source" | "list">("source");
  const [hidden, setHidden] = useState<DiagnosticSeverity[]>([]);
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
          onError: (error) =>
            toast.error(describeError(error, "The document could not be revalidated.")),
        },
      );
    },
    [specId, revalidate],
  );

  const pointers = useMemo(
    () => (diagnostics.data ?? []).map((d) => d.jsonPointer),
    [diagnostics.data],
  );
  const { lines, error: indexError } = useFindingLines(source.data?.content, pointers);

  /**
   * Findings in document order, which is the order "next" should walk. Anything the pointer
   * could not place goes last rather than being dropped: an unplaceable finding is still a
   * finding, and silently losing it would make the counts lie.
   */
  const findings = useMemo<Finding[]>(() => {
    const raw = diagnostics.data ?? [];
    return raw
      .map((d, i) => ({ ...d, id: i, severity: severityOf(d.severity), line: lines?.[i] ?? null }))
      .sort((a, b) => (a.line ?? Infinity) - (b.line ?? Infinity) || a.id - b.id);
  }, [diagnostics.data, lines]);

  const counts = useMemo(() => {
    const tally: Record<DiagnosticSeverity, number> = {
      [DiagnosticSeverity.Error]: 0,
      [DiagnosticSeverity.Warning]: 0,
      [DiagnosticSeverity.Information]: 0,
    };
    for (const f of findings) tally[f.severity] += 1;
    return tally;
  }, [findings]);

  /**
   * Whether anything at all is known about what validation found.
   *
   * Every derived value above collapses to an empty list when the diagnostics query has no
   * data — which is what a document with nothing wrong also looks like. Nothing below may
   * present the one as the other.
   */
  const findingsUnknown = diagnostics.data === undefined;

  const recorded: Record<DiagnosticSeverity, number> | undefined = totals && {
    [DiagnosticSeverity.Error]: totals.errors,
    [DiagnosticSeverity.Warning]: totals.warnings,
    [DiagnosticSeverity.Information]: totals.information,
  };

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

  // `lines === null` only means the worker has not answered yet, and it never answers when
  // there is no document to index — so a failed document load held the skeleton up forever
  // and the message written for that case was unreachable.
  const loading =
    diagnostics.isPending || source.isPending || (source.data !== undefined && lines === null);

  /** What the position counter says. "No findings" is a claim, so it waits for the list. */
  function counter() {
    // Whether anything is in hand is asked before whether the last request failed: React
    // Query keeps the previous findings through a failed refetch, and that list is on
    // screen with a real length and a real position to count through.
    if (findingsUnknown) return diagnostics.isError ? "Not loaded" : "Loading…";
    if (visible.length === 0) return "No findings";
    // The total until one is picked: "0 / 413" would claim a position in a list nobody has
    // stepped into yet.
    if (position < 0) return count(visible.length, "finding");
    return `${position + 1} / ${visible.length}`;
  }

  /**
   * What the banner over the content says, which is not one message but two.
   *
   * With nothing fetched, the empty gutter is the thing to correct. With a list still up
   * from an earlier load — a revalidate invalidates this query, and reopening the dialog
   * refetches it — saying "nothing is marked below" would be its own falsehood, and the
   * findings on screen are merely old rather than wrong.
   */
  const failure = findingsUnknown
    ? {
        title: "The findings could not be loaded",
        detail:
          "Nothing is marked below, and that is this request failing rather than a clean "
          + "document. Try again to see what validation found.",
      }
    : {
        title: "The findings could not be refreshed",
        detail:
          "What is marked below is the list from the last time it loaded. Validation may "
          + "have said something else since.",
      };

  return (
    <Dialog open={specId !== null} onOpenChange={(open) => !open && close()}>
      <DialogContent
        className="@container flex h-[92vh] w-[min(1600px,96vw)] max-w-none flex-col gap-0 overflow-hidden p-0 sm:max-w-none"
        showCloseButton={false}
      >
        {/* Three columns once the bar is wide enough, so the filters sit in its middle rather
            than wherever the title happens to end — which moves with the document's name.
            Below 64rem it wraps instead: centring needs both side columns as wide as the
            wider one, and the controls on the right measure ~360px, so anything narrower
            pushed the counter out of its column and over the chips. Measured, twice. A container query, not a
            viewport one, because what runs out of room is the bar. */}
        <div className="flex flex-wrap items-center gap-2 border-b bg-topbar px-3 py-2 @5xl:grid @5xl:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)]">
          {/* font-sans, against DialogTitle's own heading face: this title sits in a toolbar
              at 14px beside buttons, where the serif read as a different component
              rather than as the name of the thing being looked at. */}
          <DialogTitle className="mr-1 flex min-w-0 items-baseline gap-1.5 font-sans text-sm font-normal">
            <span className="font-mono text-xs text-muted-foreground tabular-nums">
              #{spec?.sequence ?? "—"}
            </span>
            <span className="truncate">{spec?.title ?? "Untitled"}</span>
          </DialogTitle>

          {/* One bordered set rather than three loose buttons, the way the specs table
              filters this dialog opens from. They toggle independently, so each carries its
              own aria-pressed — the frame groups them, it does not make them exclusive. */}
          <div className="flex items-center rounded-lg border bg-card p-0.5">
          {ORDER.map((severity) => {
            const Icon = icons[severity];

            // With no list fetched there is nothing to filter, so these stop being buttons
            // and the specs row's own totals stand in — greyed, because they are the last
            // thing recorded about this document rather than a reading of it. What must
            // never appear here is "0 errors" for a list that never arrived.
            if (findingsUnknown) {
              return (
                <span
                  key={severity}
                  className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium text-muted-foreground opacity-70"
                  title={
                    diagnostics.isError
                      ? "Recorded on the specs table. The findings themselves could not be loaded."
                      : undefined
                  }
                >
                  <Icon className="size-3.5" aria-hidden />
                  {recorded ? count(recorded[severity], nouns[severity]) : `— ${nouns[severity]}s`}
                </span>
              );
            }

            const off = hidden.includes(severity);

            return (
              <button
                key={severity}
                type="button"
                aria-pressed={!off}
                onClick={() =>
                  setHidden((prev) =>
                    prev.includes(severity)
                      ? prev.filter((s) => s !== severity)
                      : [...prev, severity],
                  )
                }
                className={cn(
                  // font-medium whether on or off: a weight that changes on click is a width
                  // that changes on click, and each toggle nudged the two beside it.
                  "flex cursor-pointer items-center gap-1.5 rounded-[calc(var(--radius)-2px)] px-2.5 py-1 text-xs font-medium transition-colors",
                  off
                    ? "text-muted-foreground hover:text-foreground"
                    : "bg-secondary text-secondary-foreground",
                )}
              >
                <Icon className={cn("size-3.5", !off && tones[severity])} aria-hidden />
                {labels[severity]}
                <span className="tabular-nums opacity-60">{counts[severity]}</span>
              </button>
            );
          })}
          </div>

          <div className="ml-auto flex items-center gap-1 @5xl:ml-0 @5xl:justify-end">
            <span className="mr-1 text-xs whitespace-nowrap tabular-nums text-muted-foreground">
              {counter()}
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
            <ButtonGroup>
              <Button
                size="xs"
                variant="outline"
                className="gap-1.5"
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
                  render={<Button size="xs" variant="outline" className="px-1.5" />}
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
            </ButtonGroup>

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

        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          {/* Stays up rather than passing as a toast: with nothing fetched, everything under
              it — a gutter with no markers, a list with no rows — is exactly what a document
              with nothing wrong looks like, and that is the one question this dialog is
              opened to answer. */}
          {diagnostics.isError && (
            // Destructive only when the failure is all there is. A refetch that failed over
            // findings still on screen is said quietly, the way the tables say it: stale
            // beats nothing, and shouting would send someone away from a list that reads
            // correctly.
            <Alert
              variant={findingsUnknown ? "destructive" : "default"}
              className="shrink-0 rounded-none border-x-0 border-t-0"
            >
              <CircleAlert className="size-4" aria-hidden />
              <AlertTitle>{failure.title}</AlertTitle>
              <AlertDescription>{failure.detail}</AlertDescription>
              <AlertAction>
                <Button
                  size="xs"
                  variant="outline"
                  disabled={diagnostics.isFetching}
                  onClick={() => diagnostics.refetch()}
                >
                  <RefreshCw
                    className={cn("size-3.5", diagnostics.isFetching && "animate-spin")}
                    aria-hidden
                  />
                  Try again
                </Button>
              </AlertAction>
            </Alert>
          )}

          {/* Not destructive, and not stacked under the findings alert: with the list
              missing there are no pointers to place, so that failure is the one to say. No
              action either — it is the document, and the parser recovers by design, so the
              lines it found are real and the rest are simply unplaced. */}
          {!diagnostics.isError && indexError !== null && (
            <Alert className="shrink-0 rounded-none border-x-0 border-t-0">
              <TriangleAlert className="size-4" aria-hidden />
              <AlertTitle>The document could not be indexed</AlertTitle>
              <AlertDescription>{indexError}</AlertDescription>
            </Alert>
          )}

          <div className="min-h-0 flex-1 overflow-hidden">
            {loading ? (
              <SourceSkeleton />
            ) : source.isError && mode === "source" ? (
              // Only in source mode, so the sentence's own promise holds: falling through in
              // list mode is what lets the list still work.
              <p className="p-6 text-sm text-muted-foreground">
                The document could not be loaded, so findings cannot be shown against it. The
                list still works.
              </p>
            ) : mode === "source" && source.data ? (
              <Suspense fallback={<SourceSkeleton />}>
                <SpecSourceView
                  content={source.data.content}
                  format={source.data.format}
                  findings={located}
                  unlocated={unlocated}
                  currentId={currentId}
                  scrollTo={scrollTo}
                />
              </Suspense>
            ) : (
              <ul ref={list} className="h-full overflow-y-auto">
                {visible.map((finding) => {
                  const Icon = icons[finding.severity];

                  return (
                    <Item
                      key={finding.id}
                      render={<li />}
                      size="xs"
                      data-current={finding.id === currentId}
                      className={cn(
                        // Rows in a scrolling list, not cards: square, and the rule between
                        // two of them is the item's own border rather than the list's
                        // divide-y, which an Item paints over with its transparent one.
                        "rounded-none [&:not(:last-child)]:border-b-border",
                        finding.id === currentId && "bg-muted/60",
                      )}
                    >
                      <ItemMedia variant="icon">
                        <Icon className={tones[finding.severity]} aria-hidden />
                      </ItemMedia>
                      <ItemContent className="min-w-0">
                        {/* A finding is read, not scanned: the message keeps the weight it had
                            and the whole of it stays on screen however long it runs. */}
                        <ItemTitle className="line-clamp-none font-normal">
                          {finding.message}
                        </ItemTitle>
                        {finding.jsonPointer && (
                          <ItemDescription className="line-clamp-none font-mono text-xs break-all">
                            {finding.jsonPointer}
                          </ItemDescription>
                        )}
                      </ItemContent>

                      {/* Unlocated findings have somewhere to go now — the block above the
                          first line — so they get the same button, labelled for where it
                          lands rather than with a line number it does not have. */}
                      <ItemActions>
                        <Button
                          size="xs"
                          variant="ghost"
                          className={cn("shrink-0 gap-1", finding.line !== null && "font-mono")}
                          onClick={() => openInSource(finding)}
                        >
                          <SquareCode className="size-3.5" aria-hidden />
                          {finding.line ?? "Top"}
                        </Button>
                      </ItemActions>
                    </Item>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
