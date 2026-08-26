import { Link, useNavigate, useParams } from "react-router-dom";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Play } from "lucide-react";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { RunStatusBadge } from "@/components/StatusBadge";
import { useRuns } from "@/api/queries";
import { describeRunProgress, formatRunDuration, isTerminal, runStatusLabel } from "@/api/types";

/** Every pipeline this project has run, newest first. */
export function ProjectRunsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const navigate = useNavigate();
  const runs = useRuns(orgId, projectId);

  return (
    <div>
      <QueryBoundary isLoading={runs.isLoading} error={runs.error}>
        {runs.data?.length ? (
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
              {runs.data.map((run) => {
                const queued = new Date(run.queuedAt);

                return (
                  <TableRow
                    key={run.id}
                    // The whole row is the target for a mouse; the link inside keeps it
                    // reachable by keyboard and nameable by a screen reader.
                    className="cursor-pointer"
                    onClick={() => navigate(`/orgs/${orgId}/runs/${run.id}`)}
                  >
                    <TableCell>
                      <Link
                        to={`/orgs/${orgId}/runs/${run.id}`}
                        // Without this every row announced just "Succeeded", leaving
                        // three identical links with nothing to choose between them.
                        aria-label={`Run ${run.resolvedVersion ?? "without a version"}, ${runStatusLabel[run.status]}, queued ${queued.toLocaleString()}`}
                      >
                        <RunStatusBadge status={run.status} />
                      </Link>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{run.resolvedVersion ?? "—"}</TableCell>
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
        ) : (
          <EmptyState
            icon={<Play className="size-8" />}
            title="No runs yet"
            description="Upload a spec and add a target, then generate your first SDK."
          />
        )}
      </QueryBoundary>
    </div>
  );
}
