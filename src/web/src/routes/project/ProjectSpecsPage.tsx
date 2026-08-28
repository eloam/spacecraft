import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ArrowLeftRight,
  CircleAlert,
  CircleCheck,
  FileCode2,
  History,
  Layers,
  MoreHorizontal,
  Package,
  SquareCode,
  Trash2,
  TriangleAlert,
} from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardFooter } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { SpecUploadCard } from "@/components/SpecUploadCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SpecFindingCounts } from "@/routes/project/SpecFindingCounts";
import { SpecValidationDialog } from "@/routes/project/SpecValidationDialog";
import { SpecDiffDialog } from "@/routes/project/SpecDiffDialog";
import { timeAgo } from "@/lib/format";
import { cn } from "@/lib/utils";
import { ApiError } from "@/api/client";
import { useDeleteSpec, usePublications, useSpecs } from "@/api/queries";
import { PublicationStatus, SpecValidationStatus } from "@/api/types";
import type { SpecVersion } from "@/api/types";

/** Bytes as something readable at a glance; the exact figure is never the point here. */
function fileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

type Filter = "all" | "valid" | "warnings" | "errors";

/**
 * The same icons the counts column uses, so a filter and what it selects look alike — and,
 * once selected, the same colour too. Unselected they stay monochrome: four coloured icons
 * competing at once is what the segmented control was meant to quieten.
 */
const filters: {
  id: Filter;
  label: string;
  Icon: typeof Layers;
  tone: string;
  matches: (s: SpecVersion) => boolean;
}[] = [
  { id: "all", label: "All", Icon: Layers, tone: "text-[var(--filter-all)]", matches: () => true },
  {
    id: "valid",
    label: "Generatable",
    Icon: CircleCheck,
    tone: "text-[var(--success)]",
    matches: (s) => s.validationStatus !== SpecValidationStatus.Invalid,
  },
  {
    id: "warnings",
    label: "Warnings",
    Icon: TriangleAlert,
    tone: "text-[var(--code-warning)]",
    matches: (s) => s.warningCount > 0,
  },
  {
    id: "errors",
    label: "Errors",
    Icon: CircleAlert,
    tone: "text-destructive",
    matches: (s) => s.errorCount > 0,
  },
];

/** Uploaded OpenAPI documents and what validation made of them. */
export function ProjectSpecsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const specs = useSpecs(orgId, projectId);
  const publications = usePublications(orgId, projectId);
  const remove = useDeleteSpec(orgId, projectId);

  const [filter, setFilter] = useState<Filter>("all");
  const [selected, setSelected] = useState<string[]>([]);
  const [openFor, setOpenFor] = useState<string | null>(null);
  const [comparing, setComparing] = useState<[SpecVersion, SpecVersion] | null>(null);
  const [deleting, setDeleting] = useState<SpecVersion | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const items = useMemo(() => specs.data?.items ?? [], [specs.data]);

  // Newest first from the API, so the first row is the one a new run would generate from.
  const latestId = items[0]?.id;

  // The listing is capped server-side. Saying so beats presenting a slice as the whole.
  const hidden = (specs.data?.total ?? 0) - items.length;

  /**
   * What each version put in a registry. Publications carry the spec version they came from,
   * so this says which document is live rather than making the reader join two tabs.
   */
  const published = useMemo(() => {
    const map = new Map<string, { label: string; url: string | null }[]>();

    for (const publication of publications.data ?? []) {
      if (!publication.specVersionId) continue;
      if (
        publication.status !== PublicationStatus.Succeeded &&
        publication.status !== PublicationStatus.AlreadyExists
      ) {
        continue;
      }

      const label = `${publication.packageName}@${publication.version}`;
      const existing = map.get(publication.specVersionId) ?? [];
      if (!existing.some((p) => p.label === label)) {
        existing.push({ label, url: publication.registryUrl });
      }
      map.set(publication.specVersionId, existing);
    }

    return map;
  }, [publications.data]);

  const visible = useMemo(
    () => items.filter(filters.find((f) => f.id === filter)!.matches),
    [items, filter],
  );

  function toggle(id: string) {
    setSelected((current) =>
      current.includes(id)
        ? current.filter((x) => x !== id)
        // Two is the whole point; picking a third drops the oldest choice rather than
        // refusing, which would leave the reader to work out why nothing happened.
        : [...current, id].slice(-2),
    );
  }

  // Resolved against what is on screen, not against everything: a filter that hides a
  // selected row must not leave Compare active on two documents nobody can see. Derived
  // rather than pruned, so changing the filter back finds the selection still there.
  const pair = useMemo(
    () =>
      selected
        .map((id) => visible.find((s) => s.id === id))
        .filter((s): s is SpecVersion => s !== undefined),
    [selected, visible],
  );

  // A YAML document and a JSON one can describe the same API and still share no line. A
  // textual diff of the two would mark every line as changed and teach nothing.
  const mixedNotations = pair.length === 2 && pair[0].format !== pair[1].format;

  function compare() {
    if (pair.length === 2 && !mixedNotations) setComparing([pair[0], pair[1]]);
  }

  function confirmDelete() {
    if (!deleting) return;

    setDeleteError(null);
    remove.mutate(deleting.id, {
      onSuccess: () => {
        toast.success(`Deleted spec #${deleting.sequence}`);
        setSelected((current) => current.filter((id) => id !== deleting.id));
        setDeleting(null);
      },
      // A refusal is the server saying a package was published from this document. It belongs
      // in the dialog, next to the button that was just pressed, not in a toast that appears
      // somewhere else and disappears.
      onError: (error) =>
        setDeleteError(
          error instanceof ApiError ? error.detail : "The spec version could not be deleted.",
        ),
    });
  }

  return (
    <div className="space-y-6">
      <SpecUploadCard orgId={orgId} projectId={projectId} onViewFindings={setOpenFor} />

      <QueryBoundary isLoading={specs.isLoading} error={specs.error}>
        {items.length ? (
          <div className="space-y-3">
            {/* Beside the table, not inside its frame: a toolbar band directly above the
                table's own header row put two banded strips back to back, which reads as a
                table inside a table. */}
            <div className="flex flex-wrap items-center gap-2">
              {/* Appears only once two versions are picked: an always-present button that does
                  nothing most of the time teaches people to ignore it. */}
              {pair.length === 2 && (
                <div className="flex items-center gap-2">
                  {mixedNotations && (
                    // Said plainly rather than by a button that vanishes: the two were just
                    // picked, and their disappearance would read as a bug.
                    <p className="text-xs text-muted-foreground">
                      A {pair[0].format.toUpperCase()} and a {pair[1].format.toUpperCase()}{" "}
                      document share no lines to compare.
                    </p>
                  )}
                  <Button
                    size="xs"
                    variant="outline"
                    className="gap-1.5"
                    disabled={mixedNotations}
                    onClick={compare}
                  >
                    <ArrowLeftRight className="size-3.5" aria-hidden />
                    Compare the two selected
                  </Button>
                </div>
              )}

              {/* One control rather than four loose buttons: a segmented set says the choices
                  are exclusive before anything is clicked. */}
              <div className="ml-auto flex items-center rounded-lg border bg-card p-0.5">
                {filters.map(({ id, label, Icon, tone, matches }) => {
                  const count = items.filter(matches).length;

                  return (
                    <button
                      key={id}
                      type="button"
                      aria-pressed={filter === id}
                      disabled={count === 0 && id !== "all"}
                      onClick={() => setFilter(id)}
                      className={cn(
                        "flex cursor-pointer items-center gap-1.5 rounded-[6px] px-2.5 py-1 text-xs transition-colors",
                        "disabled:cursor-not-allowed disabled:opacity-40",
                        filter === id
                          ? "bg-secondary font-medium text-secondary-foreground"
                          : "text-muted-foreground hover:text-foreground",
                      )}
                    >
                      <Icon className={cn("size-3.5", filter === id && tone)} aria-hidden />
                      {label}
                      <span className="tabular-nums opacity-60">{count}</span>
                    </button>
                  );
                })}
              </div>
            </div>

            <Card className="py-0">
              <CardContent className="p-0">
                <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <span className="sr-only">Select to compare</span>
                  </TableHead>
                  <TableHead>API</TableHead>
                  {/* The document's own format version, not the API's — "3.0.3", not "1.0". */}
                  <TableHead className="w-32">OpenAPI</TableHead>
                  <TableHead className="w-32">Validation</TableHead>
                  <TableHead>Published</TableHead>
                  <TableHead className="w-32">Uploaded</TableHead>
                  <TableHead className="w-24 text-right">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visible.map((spec) => {
                  const packages = published.get(spec.id) ?? [];

                  return (
                    <TableRow
                      key={spec.id}
                      className={cn("group", selected.includes(spec.id) && "bg-muted/50")}
                    >
                      <TableCell>
                        <Checkbox
                          checked={selected.includes(spec.id)}
                          onCheckedChange={() => toggle(spec.id)}
                          aria-label={`Select spec #${spec.sequence} to compare`}
                        />
                      </TableCell>

                      {/* Title, with the details that used to cost three columns of their own. */}
                      <TableCell>
                        <div className="flex items-center gap-2">
                          <span className="font-mono text-xs text-muted-foreground">
                            #{spec.sequence}
                          </span>
                          {/* The name is the obvious thing to click to read a document, and
                              was the one part of the row that did nothing. Underlined on row
                              hover rather than its own: the pointer is somewhere on the row
                              long before it reaches the six characters of a title. */}
                          <button
                            type="button"
                            onClick={() => setOpenFor(spec.id)}
                            className="cursor-pointer rounded-sm font-medium group-hover:underline focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
                          >
                            {spec.apiTitle ?? "Untitled"}
                          </button>
                          {spec.id === latestId && (
                            // Which document a new run generates from is the question this
                            // table is usually opened to answer.
                            <Badge variant="secondary" className="text-[10px]">
                              Latest
                            </Badge>
                          )}
                        </div>
                        <p className="font-mono text-xs text-muted-foreground">
                          {spec.apiVersion ?? "no version"} · {fileSize(spec.sizeInBytes)}
                        </p>
                      </TableCell>

                      <TableCell>
                        <div className="flex items-center gap-1.5">
                          <span className="font-mono text-xs">{spec.openApiVersion ?? "—"}</span>
                          <Badge variant="outline" className="font-mono text-[10px] uppercase">
                            {spec.format}
                          </Badge>
                        </div>
                      </TableCell>

                      {/* A status, not a control. Opening the document is a button of its own
                          now: as a hidden button this only worked when a spec had findings,
                          which left a clean one with no way to be read at all. */}
                      <TableCell>
                        <SpecFindingCounts
                          status={spec.validationStatus}
                          information={spec.informationCount}
                          warnings={spec.warningCount}
                          errors={spec.errorCount}
                        />
                      </TableCell>

                      <TableCell className="text-xs">
                        {publications.isError ? (
                          // Not "—": that would read as "nothing published", which is a claim
                          // this column is in no position to make when its query failed.
                          <span className="text-muted-foreground" title="The publications could not be loaded">
                            unknown
                          </span>
                        ) : packages.length === 0 ? (
                          <span className="text-muted-foreground">—</span>
                        ) : (
                          // Also what makes the delete button honest: a published version is
                          // refused, and now that is visible before the click rather than
                          // after it.
                          <div className="space-y-0.5 font-mono">
                            {packages.map(({ label, url }) => {
                              const inner = (
                                <>
                                  <Package className="size-3 shrink-0 text-muted-foreground" aria-hidden />
                                  {label}
                                </>
                              );

                              // The registry is where this actually lives; a name that cannot
                              // be followed is half an answer.
                              return url ? (
                                <a
                                  key={label}
                                  href={url}
                                  target="_blank"
                                  rel="noreferrer noopener"
                                  className="flex items-center gap-1 truncate hover:underline"
                                >
                                  {inner}
                                </a>
                              ) : (
                                <p key={label} className="flex items-center gap-1 truncate">
                                  {inner}
                                </p>
                              );
                            })}
                          </div>
                        )}
                      </TableCell>

                      <TableCell
                        className="text-xs text-muted-foreground"
                        title={new Date(spec.createdAt).toLocaleString()}
                      >
                        {timeAgo(spec.createdAt)}
                      </TableCell>

                      <TableCell className="text-right">
                        <div className="flex justify-end gap-1">
                          {/* Kept in the open: it is the one action that leaves this page, and
                              a destination is worth showing rather than hiding behind a menu. */}
                          <Button
                            size="icon-sm"
                            variant="ghost"
                            aria-label={`Runs generated from spec #${spec.sequence}`}
                            title="Runs generated from this version"
                            render={<Link to={`/orgs/${orgId}/projects/${projectId}/runs?spec=${spec.id}`} />}
                          >
                            <History className="size-4" aria-hidden />
                          </Button>

                          <DropdownMenu>
                            <DropdownMenuTrigger
                              render={<Button size="icon-sm" variant="ghost" />}
                              aria-label={`More actions for spec #${spec.sequence}`}
                            >
                              <MoreHorizontal className="size-4" aria-hidden />
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end" className="w-44">
                              {/* Still here though the title does the same: a menu is where
                                  someone looks for the full list of what a row can do. */}
                              <DropdownMenuItem onClick={() => setOpenFor(spec.id)}>
                                <SquareCode className="size-4" aria-hidden />
                                View source
                              </DropdownMenuItem>
                              <DropdownMenuSeparator />
                              <DropdownMenuItem
                                variant="destructive"
                                onClick={() => {
                                  setDeleteError(null);
                                  setDeleting(spec);
                                }}
                              >
                                <Trash2 className="size-4" aria-hidden />
                                Delete
                              </DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
                </Table>

                {visible.length === 0 && (
                  <p className="py-10 text-center text-sm text-muted-foreground">
                    No version matches this filter.
                  </p>
                )}
              </CardContent>

              {hidden > 0 && (
                <CardFooter className="border-t py-2.5">
                  <p className="text-xs text-muted-foreground">
                    Showing the {items.length} most recent of {specs.data?.total} versions.
                  </p>
                </CardFooter>
              )}
            </Card>
          </div>
        ) : (
          // The drop zone above is not an empty state: it says what to do, not what is there.
          <EmptyState
            icon={<FileCode2 className="size-8" aria-hidden />}
            title="No document yet"
            description="Upload an OpenAPI document above. Each upload is kept as a version, so you can compare them and see what every published package was generated from."
          />
        )}
      </QueryBoundary>

      <SpecValidationDialog
        orgId={orgId}
        projectId={projectId}
        specId={openFor}
        onClose={() => setOpenFor(null)}
      />

      <SpecDiffDialog
        orgId={orgId}
        projectId={projectId}
        pair={comparing}
        onClose={() => setComparing(null)}
      />

      <ConfirmDeleteDialog
        open={deleting !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDeleting(null);
            setDeleteError(null);
          }
        }}
        title={`Delete spec #${deleting?.sequence}?`}
        description="The document and everything generated from it are removed."
        consequences={[
          "The uploaded document itself",
          "Every run executed against it, with its artifacts and logs",
          "Packages already published stay in their registries",
        ]}
        isPending={remove.isPending}
        error={deleteError}
        onConfirm={confirmDelete}
      />
    </div>
  );
}
