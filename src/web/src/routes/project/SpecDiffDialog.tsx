import { Suspense, lazy, useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeftRight, ChevronDown, ChevronUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { useSpecContent } from "@/api/queries";
import type { SpecVersion } from "@/api/types";

/** The merge addon rides on the editor chunk; neither is fetched until a comparison is opened. */
const SpecDiffView = lazy(() =>
  import("@/components/SpecDiffView").then((m) => ({ default: m.SpecDiffView })),
);

function Loading() {
  return (
    <div className="grid grid-cols-2 gap-4 p-4">
      {Array.from({ length: 16 }, (_, i) => (
        <Skeleton key={i} className="h-3" style={{ width: `${35 + ((i * 19) % 55)}%` }} />
      ))}
    </div>
  );
}

/**
 * Two spec versions, side by side.
 *
 * Always older on the left whatever order they were picked in: a diff read right-to-left says
 * the opposite of what happened, and nothing on screen would say which way round it is.
 */
export function SpecDiffDialog({
  orgId,
  projectId,
  pair,
  onClose,
}: {
  orgId: string;
  projectId: string;
  pair: [SpecVersion, SpecVersion] | null;
  onClose: () => void;
}) {
  const ordered = pair
    ? ([...pair].sort((a, b) => a.sequence - b.sequence) as [SpecVersion, SpecVersion])
    : null;

  const before = useSpecContent(orgId, projectId, ordered?.[0].id ?? null);
  const after = useSpecContent(orgId, projectId, ordered?.[1].id ?? null);

  const [chunkCount, setChunkCount] = useState(0);
  const [at, setAt] = useState(-1);
  const [scrollTo, setScrollTo] = useState<{ index: number; nonce: number } | null>(null);
  const nonce = useRef(0);

  // Identity-stable, so reporting the count does not tear the merge view down and rebuild it.
  const onChunkCount = useCallback((count: number) => {
    setChunkCount(count);
    setAt(-1);
    setScrollTo(null);
  }, []);

  const step = useCallback(
    (by: 1 | -1) => {
      if (chunkCount === 0) return;

      // Wrapping both ways, and starting at either end when nothing is selected — the same
      // stepping the findings dialog uses, because it is the same gesture.
      const next = at < 0 ? (by === 1 ? 0 : chunkCount - 1) : (at + by + chunkCount) % chunkCount;

      setAt(next);
      nonce.current += 1;
      setScrollTo({ index: next, nonce: nonce.current });
    },
    [at, chunkCount],
  );

  useEffect(() => {
    if (pair === null) return;

    function onKey(event: KeyboardEvent) {
      if (event.key !== "F8") return;
      event.preventDefault();
      step(event.shiftKey ? -1 : 1);
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [pair, step]);

  const ready = before.data && after.data;
  const identical = ready && before.data.content === after.data.content;

  // The table already refuses to offer this, but the rule belongs with the thing it protects
  // rather than only with the caller that happens to know about it today.
  const mixedNotations = ready && before.data.format !== after.data.format;

  return (
    <Dialog open={pair !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className="flex h-[92vh] w-[min(1600px,96vw)] max-w-none flex-col gap-0 overflow-hidden p-0 sm:max-w-none"
        showCloseButton={false}
      >
        <div className="flex flex-wrap items-center gap-3 border-b bg-topbar px-3 py-2">
          <DialogTitle className="text-sm font-semibold">Compare</DialogTitle>

          {ordered && (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <span className="font-mono">#{ordered[0].sequence}</span>
              <ArrowLeftRight className="size-3.5" aria-hidden />
              <span className="font-mono">#{ordered[1].sequence}</span>
            </p>
          )}

          {identical && (
            // Only reachable through a stale list: the upload endpoint refuses identical bytes.
            <p className="text-sm text-muted-foreground">The two documents are identical.</p>
          )}

          <div className="ml-auto flex items-center gap-1">
            <span className="mr-1 text-xs tabular-nums text-muted-foreground">
              {chunkCount === 0
                ? "No change"
                : at < 0
                  ? `${chunkCount} change${chunkCount === 1 ? "" : "s"}`
                  : `${at + 1} / ${chunkCount}`}
            </span>
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label="Previous change"
              title="Previous change (Shift+F8)"
              disabled={chunkCount === 0}
              onClick={() => step(-1)}
            >
              <ChevronUp className="size-4" aria-hidden />
            </Button>
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label="Next change"
              title="Next change (F8)"
              disabled={chunkCount === 0}
              onClick={() => step(1)}
            >
              <ChevronDown className="size-4" aria-hidden />
            </Button>

            <Button size="xs" variant="ghost" onClick={onClose}>
              Close
            </Button>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-hidden">
          {before.isError || after.isError ? (
            <p className="p-6 text-sm text-muted-foreground">
              One of the two documents could not be loaded.
            </p>
          ) : mixedNotations ? (
            <p className="p-6 text-sm text-muted-foreground">
              One document is {before.data!.format.toUpperCase()} and the other is{" "}
              {after.data!.format.toUpperCase()}. They can describe the same API without sharing
              a single line, so a line-by-line comparison would mark all of it as changed.
            </p>
          ) : !ready ? (
            <Loading />
          ) : (
            <Suspense fallback={<Loading />}>
              <SpecDiffView
                before={before.data.content}
                after={after.data.content}
                format={after.data.format}
                scrollTo={scrollTo}
                onChunkCount={onChunkCount}
              />
            </Suspense>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
