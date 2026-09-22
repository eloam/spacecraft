import { Suspense, lazy, useCallback, useRef, useState } from "react";
import { ArrowLeftRight, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Dialog, DialogTitle } from "@/components/ui/dialog";
import { SourceDialogContent, StepButtons } from "@/components/SourceDialog";
import { useStepShortcut } from "@/hooks/useStepShortcut";
import { plural } from "@/lib/format";
import { wrapStep } from "@/lib/step";
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

  function step(by: 1 | -1) {
    const next = wrapStep(at, by, chunkCount);
    if (next === null) return;

    setAt(next);
    nonce.current += 1;
    setScrollTo({ index: next, nonce: nonce.current });
  }

  useStepShortcut(pair !== null, step);

  const ready = before.data && after.data;
  const identical = ready && before.data.content === after.data.content;
  const formats = ready ? { before: before.data.format, after: after.data.format } : null;

  // The table already refuses to offer this, but the rule belongs with the thing it protects
  // rather than only with the caller that happens to know about it today.
  const mixedNotations = formats !== null && formats.before !== formats.after;

  return (
    <Dialog open={pair !== null} onOpenChange={(open) => !open && onClose()}>
      <SourceDialogContent>
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
                  ? plural(chunkCount, "change")
                  : `${at + 1} / ${chunkCount}`}
            </span>
            <StepButtons noun="change" disabled={chunkCount === 0} onStep={step} />

            {/* An icon, like the one every other dialog closes with: the word sat in a row
                of icon buttons and read as one more tool rather than the way out. */}
            <Button size="icon-sm" variant="ghost" aria-label="Close" onClick={onClose}>
              <X className="size-4" aria-hidden />
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
              One document is {formats.before.toUpperCase()} and the other is{" "}
              {formats.after.toUpperCase()}. They can describe the same API without sharing a
              single line, so a line-by-line comparison would mark all of it as changed.
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
      </SourceDialogContent>
    </Dialog>
  );
}
