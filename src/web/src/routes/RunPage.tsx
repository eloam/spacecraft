import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Ban, Download, FileUp, Hand, MousePointerClick, Zap } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Item, ItemActions, ItemMedia, ItemTitle } from "@/components/ui/item";
import { Separator } from "@/components/ui/separator";
import { Spinner } from "@/components/ui/spinner";
import { QueryBoundary } from "@/components/QueryBoundary";
import { RunStatusIcon, StepStatusIcon } from "@/components/StatusBadge";
import { api } from "@/api/client";
import { useCancelRun, useRun, useStepLog } from "@/api/queries";
import {
  RunTrigger,
  StepStatus,
  formatSeconds,
  isTerminal,
  languageLabel,
  registryLabel,
  stepKindLabel,
  stepStatusLabel,
} from "@/api/types";
import type { ArtifactDownload } from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";
import { timeAgo } from "@/lib/format";
import { BackLink } from "@/components/BackLink";
import { LanguageMark } from "@/components/LanguageDot";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";

export function RunPage() {
  const { orgId = "", runId = "" } = useParams();
  const run = useRun(orgId, runId);
  const cancel = useCancelRun(orgId);
  const [openStep, setOpenStep] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);

  async function download() {
    // The tab is opened here, synchronously, and pointed at the artifact once the presign
    // comes back. Opening it after the await put it outside the click that asked for it:
    // Safari refuses that outright and the others once the request outlives the few seconds
    // of transient activation — and a refused popup returns null instead of throwing, so
    // the button did nothing at all and said nothing either.
    //
    // "about:blank" and no noopener, both deliberately: window.open returns null whenever
    // noopener is passed, which would leave no handle to point anywhere. The reverse
    // reference is severed by hand instead.
    const tab = window.open("about:blank", "_blank");
    if (tab) tab.opener = null;

    setDownloading(true);
    try {
      const result = await api.get<ArtifactDownload>(`/organizations/${orgId}/runs/${runId}/artifact`);
      if (tab) {
        // A presigned URL: the browser fetches the bytes straight from object storage.
        // replace, so the blank placeholder is not a Back step in the new tab.
        tab.location.replace(result.url);
      } else {
        toast.error("Your browser blocked the download tab.", {
          // The retry runs inside a click of its own, which is what the blocker wanted.
          action: {
            label: "Open",
            onClick: () => window.open(result.url, "_blank", "noopener,noreferrer"),
          },
        });
      }
    } catch (err) {
      tab?.close();
      toast.error(err instanceof ApiError ? err.detail : "Could not fetch the artifact.");
    } finally {
      setDownloading(false);
    }
  }

  return (
    <div className="space-y-6">
      {/* The runs list is the organization's now, narrowed back to the project this run
          belongs to. It used to point at /projects/{id}/runs, a route that stopped existing
          when the tab moved — React Router matched the catch-all and sent "All runs" to the
          organizations page. */}
      <div>
        <BackLink
          to={run.data ? `/orgs/${orgId}/runs?project=${run.data.apiProjectId}` : `/orgs/${orgId}/runs`}
          className="mb-2"
        >
          All runs
        </BackLink>

        <div className="flex flex-wrap items-start justify-between gap-3">
          {/* The icon sits outside the column holding both lines, rather than inside the
              heading: padded to match, the subtitle would have hung under the icon whenever
              there is no run yet to have a status. This way they line up because they share
              an edge. */}
          <div className="flex min-w-0 items-start gap-2.5">
            {run.data && (
              // mt-1 rather than items-center: aligned to the cap height of a 24px serif
              // heading, where centring on the line box sat it visibly low.
              <RunStatusIcon status={run.data.status} className="mt-1 size-6" />
            )}

            <div className="min-w-0">
              <h1 className="font-heading text-2xl font-medium tracking-tight">
                Pipeline run
                {run.data && (
                  <>
                    {/* The package this run produces, so the title names the thing rather
                        than the page, with the version it resolved beside it — one line
                        answering "what was built, and which version of it". */}
                    <span className="text-muted-foreground"> — {run.data.targetPackageName}</span>
                    {run.data.resolvedVersion && (
                      <span className="ml-2 font-mono text-base text-muted-foreground">
                        v{run.data.resolvedVersion}
                      </span>
                    )}
                  </>
                )}
              </h1>

              {/* The project belongs here, not in the table: that table is the SDK targets
                  one, and a project is not a property of a target. */}
              {/* The run's own facts, where the id used to be. The id is in the address
                  bar for anyone who needs it; under a title it was a line of hexadecimal
                  answering a question nobody had.

                  What started the run has its own column now, where the two answers sit
                  side by side instead of one of them being inferred from silence. */}
              {run.data && (
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
                  <Link
                    to={`/orgs/${orgId}/projects/${run.data.apiProjectId}`}
                    className="text-foreground underline-offset-4 hover:underline"
                  >
                    Spec #{run.data.specSequence}
                  </Link>
                  <span aria-hidden>·</span>
                  <Tooltip>
                    <TooltipTrigger render={<span />}>
                      Queued {timeAgo(run.data.queuedAt)}
                    </TooltipTrigger>
                    <TooltipContent>{new Date(run.data.queuedAt).toLocaleString()}</TooltipContent>
                  </Tooltip>
                  {run.data.startedAt && (
                    <>
                      <span aria-hidden>·</span>
                      {/* Queued, waited, ran: "slow" and "sat in the queue" look identical
                          without the middle number. */}
                      <span>
                        waited{" "}
                        {formatSeconds(
                          (new Date(run.data.startedAt).getTime() -
                            new Date(run.data.queuedAt).getTime()) / 1000,
                        )}
                      </span>
                    </>
                  )}
                  {run.data.startedAt && run.data.completedAt && (
                    <>
                      <span aria-hidden>·</span>
                      <span>
                        ran{" "}
                        {formatSeconds(
                          (new Date(run.data.completedAt).getTime() -
                            new Date(run.data.startedAt).getTime()) / 1000,
                        )}
                      </span>
                    </>
                  )}
                </div>
              )}
            </div>
          </div>

          {/* Where every other page puts its primary action — New project, Add target,
              Generate SDK. The status rides with them because it is what decides which of
              them are there: an artifact exists only once a run has produced one, and Cancel
              only while it has not finished. */}
          {run.data && (
            <div className="flex shrink-0 items-center gap-2">
              {run.data.attemptCount > 1 && (
                <span className="text-xs text-muted-foreground">
                  attempt {run.data.attemptCount}
                </span>
              )}
              {run.data.hasArtifact && (
                <Button variant="outline" size="sm" onClick={download} disabled={downloading}>
                  {/* The spinner replaces the icon and the label stays put, as on the
                      Generate button: a label that changes is a width that changes, and this
                      one sits next to Cancel in a right-aligned row that would slide sideways
                      for as long as the presign takes. */}
                  {downloading ? <Spinner /> : <Download className="size-4" aria-hidden />}
                  Artifact
                </Button>
              )}
              {!isTerminal(run.data.status) && (
                <Button
                  variant="destructive"
                  size="sm"
                  onClick={() =>
                    cancel.mutate(runId, {
                      onSuccess: () => toast.success("Run cancelled"),
                      onError: (err) =>
                        toast.error(
                          err instanceof ApiError ? err.detail : "Could not cancel the run.",
                        ),
                    })
                  }
                  disabled={cancel.isPending}
                >
                  <Ban className="size-4" aria-hidden />
                  Cancel
                </Button>
              )}
            </div>
          )}
        </div>
      </div>

      {/* This page polls every two seconds until the run settles, so a single dropped poll
          must not replace a perfectly good run with a full-width error. */}
      <QueryBoundary
        isLoading={run.isLoading}
        error={run.error}
        hasData={run.data !== undefined}
        rows={4}
      >
        {run.data && (
          <>
            <Card className="py-0">
              {/* The SDK targets table, header and one row, rendered exactly as it is
                  there — same columns, same two-line SDK cell, same registry badge, same
                  publishing pair, same label-over-address. Someone arriving from that tab
                  should not have to re-learn how to read the same five facts.

                  Read off the run, not the target: the target may since have been renamed,
                  repointed, switched between automatic and manual, or deleted, and a page
                  describing history has to keep saying what happened. */}
              <CardContent className="p-0">
                <Table>
                  <TableHeader>
                    <TableRow>
                      {/* Ahead of the SDK: the project is what someone landing here from a
                          list of runs needs first, and it was in eleven-pixel grey beside a
                          guid. */}
                      <TableHead>Project</TableHead>
                      {/* Beside the project, ahead of the target's own columns: both are
                          facts about this run, and what follows describes the target. */}
                      <TableHead className="w-40">Started</TableHead>
                      <TableHead>SDK</TableHead>
                      <TableHead className="w-32">Registry</TableHead>
                      <TableHead className="w-44">Publishing</TableHead>
                      {/* Only for a run that published. A destination shown for one that did
                          not is a URL in evidence beside a package that never reached it —
                          the exact misreading the Publishing cell was just fixed to avoid. */}
                      {run.data.publishRequested && <TableHead className="w-64">Publishes to</TableHead>}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {/* Nothing to click here — one row describing the run, not a list
                        to pick from. The primitive lifts a row on hover because rows
                        are usually targets; this one is a fact. */}
                    <TableRow className="hover:bg-transparent">
                      <TableCell>
                        <Link
                          to={`/orgs/${orgId}/projects/${run.data.apiProjectId}/targets`}
                          className="font-medium underline-offset-4 hover:underline"
                        >
                          {run.data.projectName ?? "—"}
                        </Link>
                      </TableCell>

                      <TableCell>
                        {/* Named in full rather than "Manual"/"Automatic": those two words
                            already answer a different question one column to the right, and
                            a reader meeting both would have to work out which axis each was
                            on. */}
                        {run.data.trigger === RunTrigger.SpecUpload ? (
                          <span className="flex items-center gap-1.5 text-xs">
                            <FileUp className="size-3.5 text-[var(--code-warning)]" aria-hidden />
                            By a spec upload
                          </span>
                        ) : (
                          <span className="flex items-center gap-1.5 text-xs">
                            <MousePointerClick className="size-3.5 text-muted-foreground" aria-hidden />
                            By hand
                          </span>
                        )}
                      </TableCell>

                      <TableCell>
                        <div className="flex items-center gap-3">
                          <LanguageMark language={run.data.targetLanguage} />
                          <div className="flex min-w-0 flex-col">
                            <span className="font-medium">
                              {languageLabel[run.data.targetLanguage] ?? "Unknown"}
                            </span>
                            <span className="truncate font-mono text-xs text-muted-foreground">
                              {run.data.targetPackageName}
                            </span>
                          </div>
                        </div>
                      </TableCell>

                      <TableCell>
                        <Badge variant="outline" className="font-mono text-[10px]">
                          {registryLabel[run.data.targetRegistry] ?? "—"}
                        </Badge>
                      </TableCell>

                      <TableCell>
                        {/* This run first, the target underneath. Borrowed from the SDK
                            targets table, the cell led with the target's mode and corrected
                            itself below — backwards on a page about one run, and it asked
                            the reader to know that the first line was not about it.

                            "on"/"off" rather than "published": whether it went out is what
                            the Publish step says, and this says what the run was asked to
                            do, which is the thing the target can disagree with. */}
                        <div className="flex flex-col gap-0.5">
                          <span className="flex items-center gap-1.5 text-xs">
                            {run.data.publishRequested ? (
                              <Zap className="size-3.5 text-[var(--code-warning)]" aria-hidden />
                            ) : (
                              <Hand className="size-3.5 text-[var(--success)]" aria-hidden />
                            )}
                            Publishing {run.data.publishRequested ? "on" : "off"}
                          </span>
                          <span className="text-[11px] text-muted-foreground">
                            Target publishes{" "}
                            {run.data.targetAutoPublish ? "automatically" : "manually"}
                          </span>
                        </div>
                      </TableCell>

                      {run.data.publishRequested && (
                      <TableCell>
                        {run.data.targetRegistryUrl ? (
                          <div className="max-w-[16rem]">
                            <p className="truncate">{run.data.targetCredentialLabel ?? "—"}</p>
                            <Tooltip>
                              <TooltipTrigger
                                render={
                                  <p className="truncate font-mono text-xs text-muted-foreground" />
                                }
                              >
                                {run.data.targetRegistryUrl}
                              </TooltipTrigger>
                              {/* break-all: an unbroken URL cannot wrap, so the tooltip
                                  would clip the address it was opened to reveal. */}
                              <TooltipContent className="break-all">
                                {run.data.targetRegistryUrl}
                              </TooltipContent>
                            </Tooltip>
                          </div>
                        ) : (
                          <span className="text-muted-foreground">Nowhere — no credential</span>
                        )}
                      </TableCell>
                      )}
                    </TableRow>
                  </TableBody>
                </Table>

              </CardContent>

              {run.data.failureReason && (
                <CardContent>
                  <pre className="overflow-x-auto rounded-lg bg-destructive/5 p-3 text-xs whitespace-pre-wrap text-destructive">
                    {run.data.failureReason}
                  </pre>
                </CardContent>
              )}
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-base">Steps</CardTitle>
              </CardHeader>
              <CardContent className="space-y-0 p-0">
                {run.data.steps.map((step, index) => {
                  const name = stepKindLabel[step.kind] ?? String(step.kind);
                  const isOpen = openStep === name;

                  return (
                    <div key={step.kind}>
                      {index > 0 && <Separator />}
                      <div className="px-6 py-3">
                        {/* The row is the Item, but the padding stays on the block around it:
                            the failure reason and the unfolded log sit under the row and share
                            its indent, which a padded row would push them out of. */}
                        <Item className="gap-3 p-0">
                          <ItemMedia variant="icon">
                            <StepStatusIcon status={step.status} />
                          </ItemMedia>
                          <ItemTitle>{name}</ItemTitle>
                          <span className="text-xs text-muted-foreground">
                            {stepStatusLabel[step.status]}
                          </span>

                          {step.durationSeconds != null && (
                            <span className="text-xs text-muted-foreground">
                              {step.durationSeconds.toFixed(1)}s
                            </span>
                          )}

                          {step.hasLog && (
                            <ItemActions className="ml-auto">
                              <Button
                                variant="ghost"
                                size="xs"
                                onClick={() => setOpenStep(isOpen ? null : name)}
                              >
                                {isOpen ? "Hide log" : "View log"}
                              </Button>
                            </ItemActions>
                          )}
                        </Item>

                        {step.failureReason && step.status === StepStatus.Failed && (
                          // wrap-anywhere, because Card clips what overflows it: a digest or
                          // a long path in the reason was cut off silently, with no
                          // scrollbar to say the sentence went on.
                          <p className="mt-2 pl-7 text-xs whitespace-pre-wrap wrap-anywhere text-destructive">
                            {step.failureReason}
                          </p>
                        )}

                        {isOpen && <StepLog orgId={orgId} runId={runId} step={name} />}
                      </div>
                    </div>
                  );
                })}
              </CardContent>
            </Card>
          </>
        )}
      </QueryBoundary>
    </div>
  );
}

function StepLog({ orgId, runId, step }: { orgId: string; runId: string; step: string }) {
  const log = useStepLog(orgId, runId, step, true);

  // "View log" is only rendered once the server has said this step has one, so anything
  // other than a 404 is a failed request, not a missing log. useStepLog does not retry, so
  // one dropped response used to land on "no stored log" — telling someone debugging a
  // failed run that the evidence does not exist, when a second try would have shown it.
  const missing = log.error instanceof ApiError && log.error.status === 404;

  return (
    <div className="mt-3 pl-7">
      {log.isLoading && <p className="text-xs text-muted-foreground">Loading log…</p>}

      {log.error &&
        (missing ? (
          <p className="text-xs text-destructive">This step has no stored log.</p>
        ) : (
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-xs text-destructive">The log could not be loaded.</p>
            <Button
              variant="outline"
              size="xs"
              onClick={() => log.refetch()}
              disabled={log.isFetching}
            >
              {log.isFetching ? "Retrying…" : "Try again"}
            </Button>
          </div>
        ))}

      {log.data && (
        // Secrets are stripped on the way out of the sandbox, so this is safe to render.
        <pre className="max-h-96 overflow-auto rounded-lg bg-muted p-3 font-mono text-xs whitespace-pre-wrap">
          {log.data}
        </pre>
      )}
    </div>
  );
}
