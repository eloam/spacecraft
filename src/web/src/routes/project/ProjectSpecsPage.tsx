import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ArrowLeftRight, FileCode2, History, MoreHorizontal, SquareCode, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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
import { SpecValidationBadge } from "@/routes/project/SpecValidationBadge";
import { SpecValidationDialog } from "@/routes/project/SpecValidationDialog";
import { SpecDiffDialog } from "@/routes/project/SpecDiffDialog";
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

/**
 * "2 days ago" rather than a timestamp: this column is scanned for recency, not read for a
 * precise moment. The exact value stays on the title attribute for when it is.
 */
function timeAgo(iso: string) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ["second", 60], ["minute", 60], ["hour", 24], ["day", 30], ["month", 12], ["year", Infinity],
  ];

  let value = seconds;
  for (const [unit, size] of units) {
    if (Math.abs(value) < size) {
      return new Intl.RelativeTimeFormat(undefined, { numeric: "auto" }).format(-value, unit);
    }
    value = Math.round(value / size);
  }
  return new Date(iso).toLocaleDateString();
}

type Filter = "all" | "valid" | "warnings" | "errors";

const filters: { id: Filter; label: string; matches: (s: SpecVersion) => boolean }[] = [
  { id: "all", label: "All", matches: () => true },
  { id: "valid", label: "Generatable", matches: (s) => s.validationStatus !== SpecValidationStatus.Invalid },
  { id: "warnings", label: "Warnings", matches: (s) => s.warningCount > 0 },
  { id: "errors", label: "Errors", matches: (s) => s.errorCount > 0 },
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

  // Newest first from the API, so the first row is the one a new run would generate from.
  const latestId = specs.data?.[0]?.id;

  /**
   * What each version put in a registry. Publications carry the spec version they came from,
   * so this says which document is live rather than making the reader join two tabs.
   */
  const published = useMemo(() => {
    const map = new Map<string, string[]>();

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
      if (!existing.includes(label)) existing.push(label);
      map.set(publication.specVersionId, existing);
    }

    return map;
  }, [publications.data]);

  const visible = useMemo(
    () => (specs.data ?? []).filter(filters.find((f) => f.id === filter)!.matches),
    [specs.data, filter],
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

  const pair = useMemo(
    () =>
      selected
        .map((id) => (specs.data ?? []).find((s) => s.id === id))
        .filter((s): s is SpecVersion => s !== undefined),
    [selected, specs.data],
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
      <SpecUploadCard orgId={orgId} projectId={projectId} />

      <QueryBoundary isLoading={specs.isLoading} error={specs.error}>
        {specs.data?.length ? (
          <>
            <div className="flex flex-wrap items-center gap-2">
              {filters.map((option) => {
                const count = (specs.data ?? []).filter(option.matches).length;

                return (
                  <Button
                    key={option.id}
                    size="xs"
                    variant={filter === option.id ? "secondary" : "ghost"}
                    aria-pressed={filter === option.id}
                    disabled={count === 0 && option.id !== "all"}
                    onClick={() => setFilter(option.id)}
                  >
                    {option.label}
                    <span className="ml-1 tabular-nums text-muted-foreground">{count}</span>
                  </Button>
                );
              })}

              {/* Appears only once two versions are picked: an always-present button that does
                  nothing most of the time teaches people to ignore it. */}
              {pair.length === 2 && (
                <div className="ml-auto flex items-center gap-2">
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
            </div>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <span className="sr-only">Select to compare</span>
                  </TableHead>
                  <TableHead className="w-12">#</TableHead>
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

                      <TableCell className="font-mono text-xs text-muted-foreground">
                        {spec.sequence}
                      </TableCell>

                      {/* Title, then the details that used to cost two columns of their own. */}
                      <TableCell>
                        <div className="flex items-center gap-2">
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
                        <SpecValidationBadge
                          status={spec.validationStatus}
                          information={spec.informationCount}
                          warnings={spec.warningCount}
                          errors={spec.errorCount}
                        />
                      </TableCell>

                      <TableCell className="text-xs">
                        {packages.length === 0 ? (
                          <span className="text-muted-foreground">—</span>
                        ) : (
                          // Also what makes the delete button honest: a published version is
                          // refused, and now that is visible before the click rather than
                          // after it.
                          <div className="space-y-0.5 font-mono">
                            {packages.map((label) => (
                              <p key={label} className="truncate">{label}</p>
                            ))}
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
              <p className="py-6 text-center text-sm text-muted-foreground">
                No version matches this filter.
              </p>
            )}
          </>
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
