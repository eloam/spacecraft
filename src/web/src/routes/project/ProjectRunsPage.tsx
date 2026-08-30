import { useMemo } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Play, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { RunStatusBadge } from "@/components/StatusBadge";
import { useRuns, useSpecs } from "@/api/queries";
import { describeRunProgress, formatRunDuration, isTerminal, runStatusLabel } from "@/api/types";

/** Every pipeline this project has run, newest first. */
export function ProjectRunsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const [search, setSearch] = useSearchParams();
  const runs = useRuns(orgId, projectId);

  // ?spec=<id> is how the specs table hands over: "the runs of this version". Filtered here
  // rather than server-side because the page already holds every run of the project.
  const specFilter = search.get("spec");
  const specs = useSpecs(orgId, projectId);
  const spec = specs.data?.items.find((s) => s.id === specFilter);

  const visible = useMemo(
    () => (runs.data ?? []).filter((run) => !specFilter || run.specVersionId === specFilter),
    [runs.data, specFilter],
  );

  return (
    <div className="space-y-4">
      {specFilter && (
        <div className="flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">
            Runs generated from spec{spec ? ` #${spec.sequence}` : ""}
            {spec?.apiVersion ? ` (${spec.apiVersion})` : ""}
          </span>
          <Button
            size="xs"
            variant="ghost"
            className="gap-1"
            onClick={() => {
              search.delete("spec");
              setSearch(search, { replace: true });
            }}
          >
            <X className="size-3.5" aria-hidden />
            Show all runs
          </Button>
        </div>
      )}

      {/* This list polls every two seconds while a run is in flight, so a single dropped
          poll must not take the table with it. */}
      <QueryBoundary
        isLoading={runs.isLoading}
        error={runs.error}
        hasData={runs.data !== undefined}
      >
        {visible.length ? (
          // Framed like the specs, targets, credentials and publications tables.
          <Card className="py-0">
            <CardContent className="p-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Version</TableHead>
                    <TableHead>Progress</TableHead>
                    <TableHead className="text-right">Queued</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {visible.map((run) => {
                    const queued = new Date(run.queuedAt);

                    return (
                      // relative, so the link below can stretch over the whole row. The row
                      // used to navigate from its own onClick, which meant the click on the
                      // badge pushed the same URL twice (Back then needed two presses), a
                      // Cmd-click on it opened a tab *and* moved this one, and everywhere
                      // else on the row there was no hover target and no middle-click.
                      <TableRow key={run.id} className="relative">
                        <TableCell>
                          <Link
                            to={`/orgs/${orgId}/runs/${run.id}`}
                            className="after:absolute after:inset-0 after:content-['']"
                            // Without this every row announced just "Succeeded", leaving
                            // three identical links with nothing to choose between them.
                            aria-label={`Run ${run.resolvedVersion ?? "without a version"}, ${runStatusLabel[run.status]}, queued ${queued.toLocaleString()}`}
                          >
                            <RunStatusBadge status={run.status} />
                          </Link>
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {run.resolvedVersion ?? "—"}
                        </TableCell>
                        <TableCell className="text-xs text-muted-foreground">
                          {isTerminal(run.status)
                            ? formatRunDuration(run)
                            : describeRunProgress(run)}
                        </TableCell>
                        <TableCell className="text-right text-xs text-muted-foreground">
                          {queued.toLocaleString()}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : specFilter ? (
          // Distinguished from an empty project: nothing ran from *this* version, which is
          // an answer rather than an absence.
          <EmptyState
            icon={<Play aria-hidden />}
            title="No run from this version"
            description="Nothing has been generated from this document yet."
          />
        ) : (
          <EmptyState
            icon={<Play aria-hidden />}
            title="No runs yet"
            description="Upload a spec and add a target, then generate your first SDK."
          />
        )}
      </QueryBoundary>
    </div>
  );
}
