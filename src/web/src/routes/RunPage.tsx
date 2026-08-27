import { useState } from "react";
import { useParams } from "react-router-dom";
import { Ban, Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { QueryBoundary } from "@/components/QueryBoundary";
import { RunStatusBadge, StepStatusIcon } from "@/components/StatusBadge";
import { api } from "@/api/client";
import { useCancelRun, useRun, useStepLog } from "@/api/queries";
import { StepStatus, isTerminal, stepKindLabel, stepStatusLabel } from "@/api/types";
import type { ArtifactDownload } from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";
import { BackLink } from "@/components/BackLink";

export function RunPage() {
  const { orgId = "", runId = "" } = useParams();
  const run = useRun(orgId, runId);
  const cancel = useCancelRun(orgId);
  const [openStep, setOpenStep] = useState<string | null>(null);

  async function download() {
    try {
      const result = await api.get<ArtifactDownload>(`/organizations/${orgId}/runs/${runId}/artifact`);
      // A presigned URL: the browser fetches the bytes straight from object storage.
      window.open(result.url, "_blank", "noopener,noreferrer");
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail : "Could not fetch the artifact.");
    }
  }

  return (
    <div className="space-y-6">
      {/* Same shape as a project page. The run knows its project, so it goes back to the
          list this run was opened from — and only falls back to the organization while that
          is still loading, where "All runs" would name a list it cannot yet link to. */}
      <div>
        <BackLink
          to={
            run.data
              ? `/orgs/${orgId}/projects/${run.data.apiProjectId}/runs`
              : `/orgs/${orgId}`
          }
          className="mb-2"
        >
          {run.data ? "All runs" : "All projects"}
        </BackLink>

        <h1 className="text-2xl font-semibold tracking-tight">Pipeline run</h1>
        <p className="font-mono text-xs text-muted-foreground">{runId}</p>
      </div>

      <QueryBoundary isLoading={run.isLoading} error={run.error} rows={4}>
        {run.data && (
          <>
            <Card>
              <CardHeader>
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex items-center gap-3">
                    <RunStatusBadge status={run.data.status} />
                    {run.data.resolvedVersion && (
                      <span className="font-mono text-sm text-muted-foreground">
                        v{run.data.resolvedVersion}
                      </span>
                    )}
                    {run.data.attemptCount > 1 && (
                      <span className="text-xs text-muted-foreground">
                        attempt {run.data.attemptCount}
                      </span>
                    )}
                  </div>

                  <div className="flex gap-2">
                    {run.data.hasArtifact && (
                      <Button variant="outline" size="sm" onClick={download}>
                        <Download className="size-4" aria-hidden />
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
                </div>
              </CardHeader>

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
                        <div className="flex items-center gap-3">
                          <StepStatusIcon status={step.status} />
                          <span className="text-sm font-medium">{name}</span>
                          <span className="text-xs text-muted-foreground">
                            {stepStatusLabel[step.status]}
                          </span>

                          {step.durationSeconds != null && (
                            <span className="text-xs text-muted-foreground">
                              {step.durationSeconds.toFixed(1)}s
                            </span>
                          )}

                          {step.hasLog && (
                            <Button
                              variant="ghost"
                              size="xs"
                              className="ml-auto"
                              onClick={() => setOpenStep(isOpen ? null : name)}
                            >
                              {isOpen ? "Hide log" : "View log"}
                            </Button>
                          )}
                        </div>

                        {step.failureReason && step.status === StepStatus.Failed && (
                          <p className="mt-2 pl-7 text-xs whitespace-pre-wrap text-destructive">
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

  return (
    <div className="mt-3 pl-7">
      {log.isLoading && <p className="text-xs text-muted-foreground">Loading log…</p>}
      {log.error && <p className="text-xs text-destructive">This step has no stored log.</p>}
      {log.data && (
        // Secrets are stripped on the way out of the sandbox, so this is safe to render.
        <pre className="max-h-96 overflow-auto rounded-lg bg-muted p-3 font-mono text-xs whitespace-pre-wrap">
          {log.data}
        </pre>
      )}
    </div>
  );
}
