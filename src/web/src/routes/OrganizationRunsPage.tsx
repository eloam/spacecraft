import { useMemo, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import { Play, X } from "lucide-react";
import { GenerateDialog } from "@/components/GenerateDialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { TableCard } from "@/components/TableCard";
import { RunStatusBadge } from "@/components/StatusBadge";
import { useOrganizations, useProjects, useRuns, useSpecs } from "@/api/queries";
import { isTerminal, runStatusLabel } from "@/api/types";
import { describeRunProgress, formatDateTime, formatRunDuration } from "@/lib/format";

/** The value the Select carries when no project is chosen; "" would read as unset. */
const ALL = "all";

/**
 * Every pipeline this organization has run, newest first.
 *
 * Organization-scoped rather than per project, because that is the question people actually
 * ask of it — "what is running right now", not "what is running in this one project". The
 * API was already shaped that way: the endpoint takes an optional projectId and the response
 * carries the project's name, so a run has never needed a project page to be read.
 */
export function OrganizationRunsPage() {
  const { orgSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const organization = useOrganizations().data?.find((o) => o.id === orgId);
  const [search, setSearch] = useSearchParams();
  const [generateOpen, setGenerateOpen] = useState(false);

  // ?project=<slug> narrows to one project, and the SDK targets page hands over that way.
  // Sent to the API rather than filtered here: the listing is capped server-side, so
  // filtering afterwards would quietly drop the older runs of a busy project.
  const rawProjectFilter = search.get("project");
  const projects = useProjects(orgId);

  // A slug in the address, an id in the request. Links minted before runs carried their
  // project's slug put an id here, so both forms are accepted and normalised to the slug the
  // Select matches its options on.
  const projectFilter =
    rawProjectFilter && (projects.data ?? []).some((p) => p.slug === rawProjectFilter)
      ? rawProjectFilter
      : ((projects.data ?? []).find((p) => p.id === rawProjectFilter)?.slug ?? rawProjectFilter);

  // What the API is asked with. It takes a Guid: sending it a slug filtered nothing, which
  // is why choosing a project used to widen the list instead of narrowing it.
  const projectFilterId = useProjectId(orgId, projectFilter ?? "");
  // No filter asks for every run; a filter whose id is still "" asks for nothing yet, rather
  // than for every run while the slug resolves.
  const runs = useRuns(orgId, projectFilter ? projectFilterId : undefined);

  // ?spec=<id> is how the specs table hands over: "the runs of this version". Filtered here,
  // since the API has no spec parameter and a project's runs are already in hand.
  const specFilter = search.get("spec");
  const specs = useSpecs(orgId, projectFilterId);
  const spec = specs.data?.items.find((s) => s.id === specFilter);

  const visible = useMemo(
    () => (runs.data ?? []).filter((run) => !specFilter || run.specVersionId === specFilter),
    [runs.data, specFilter],
  );

  function setParam(key: string, value: string | null) {
    if (value) search.set(key, value);
    else search.delete(key);
    // A spec belongs to the project it was uploaded to, so widening to every project cannot
    // keep a filter that names one document inside one of them.
    if (key === "project" && !value) search.delete("spec");
    setSearch(search, { replace: true });
  }

  return (
    <div className="space-y-6">
      <title>
        {organization ? `Runs — ${organization.name} — SpaceCraft` : `Runs — SpaceCraft`}
      </title>

      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-heading text-2xl font-medium tracking-tight">Runs</h1>
          <p className="text-sm text-muted-foreground">
            Every pipeline this organization has run, newest first.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Select
            value={projectFilter ?? ALL}
            onValueChange={(value) => {
              // null is Base UI's "cleared", which this select does not offer; coerced to a
              // string it put ?project=null in the address.
              if (value === null) return;
              setParam("project", value === ALL ? null : value);
            }}
          >
            <SelectTrigger size="sm" className="w-56" aria-label="Filter by project">
              {/* The label is computed rather than left to SelectValue: Base UI falls back to
                  the raw value when it cannot resolve the selected item, which printed "all"
                  where the option reads "All projects". */}
              <SelectValue>
                {projectFilter
                  ? ((projects.data ?? []).find((p) => p.slug === projectFilter)?.name ??
                    projectFilter)
                  : "All projects"}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All projects</SelectItem>
              {(projects.data ?? []).map((project) => (
                <SelectItem key={project.id} value={project.slug}>
                  {project.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          {/* The filter already names a project when one is chosen, so this button reads it
              rather than asking again. On "All projects" it opens the same dialog, which
              starts by asking — the trip through a project page is what this replaces. */}
          <Button onClick={() => setGenerateOpen(true)}>
            <Play className="size-4" aria-hidden />
            Generate SDK
          </Button>
        </div>
      </div>

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
            onClick={() => setParam("spec", null)}
          >
            <X className="size-3.5" aria-hidden />
            Show all runs
          </Button>
        </div>
      )}

      {/* hasData: this list polls every two seconds while a run is in flight, so a single
          dropped poll must not take the table with it. isPending rather than isLoading: the
          query waits, disabled, for a project filter's id, and isLoading is `isPending &&
          isFetching`, false while disabled, so a cold load under a filter showed "No runs in
          this project" before the skeleton. A filter change keeps the last list up (see
          useRuns). */}
      <QueryBoundary
        isLoading={runs.isPending}
        error={runs.error}
        hasData={runs.data !== undefined}
      >
        {visible.length ? (
          <TableCard>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Status</TableHead>
                  {/* Named on every row, not only when several projects are in view: a run
                      is read one line at a time, and "which project was that" is the first
                      thing asked of a list that spans them. */}
                  <TableHead>Project</TableHead>
                  <TableHead>Version</TableHead>
                  <TableHead>Progress</TableHead>
                  <TableHead className="text-right">Queued</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visible.map((run) => (
                  // relative, so the link below can stretch over the whole row. The row
                  // used to navigate from its own onClick, which meant the click on the
                  // badge pushed the same URL twice (Back then needed two presses), a
                  // Cmd-click on it opened a tab *and* moved this one, and everywhere
                  // else on the row there was no hover target and no middle-click.
                  <TableRow key={run.id} className="relative">
                    <TableCell>
                      <Link
                        to={`/orgs/${orgSlug}/runs/${run.id}`}
                        className="after:absolute after:inset-0 after:content-['']"
                        // Without this every row announced just "Succeeded", leaving
                        // three identical links with nothing to choose between them.
                        aria-label={`Run ${run.resolvedVersion ?? "without a version"} of ${run.projectName ?? "an unnamed project"}, ${runStatusLabel[run.status]}, queued ${formatDateTime(run.queuedAt)}`}
                      >
                        <RunStatusBadge status={run.status} />
                      </Link>
                    </TableCell>
                    <TableCell className="max-w-[14rem] truncate">
                      {run.projectName ?? "—"}
                    </TableCell>
                    <TableCell className="font-mono text-xs tabular-nums">
                      {run.resolvedVersion ?? "—"}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {isTerminal(run.status) ? formatRunDuration(run) : describeRunProgress(run)}
                    </TableCell>
                    <TableCell className="text-right text-xs whitespace-nowrap text-muted-foreground">
                      <time dateTime={run.queuedAt}>{formatDateTime(run.queuedAt)}</time>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableCard>
        ) : specFilter ? (
          // Distinguished from an empty organization: nothing ran from *this* version, which
          // is an answer rather than an absence.
          <EmptyState
            icon={<Play aria-hidden />}
            title="No run from this version"
            description="Nothing has been generated from this document yet."
          />
        ) : projectFilter ? (
          <EmptyState
            icon={<Play aria-hidden />}
            title="No runs in this project"
            description="Other projects may have run. Widen the filter to see them."
          />
        ) : (
          <EmptyState
            icon={<Play aria-hidden />}
            title="No runs yet"
            description="Upload a spec and add a target, then generate your first SDK."
          />
        )}
      </QueryBoundary>

      <GenerateDialog
        key={projectFilter ?? ""}
        orgId={orgId}
        orgSlug={orgSlug}
        // undefined, not "": the dialog reads undefined as "no project fixed, ask for one".
        projectSlug={projectFilter ?? undefined}
        open={generateOpen}
        onOpenChange={setGenerateOpen}
      />
    </div>
  );
}
