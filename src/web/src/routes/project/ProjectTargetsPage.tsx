import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import { ExternalLink, Hand, History, Layers, MoreHorizontal, Package, Pencil, Plus, Trash2, Zap } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { TargetDialog } from "@/routes/project/TargetDialog";
import { ApiError } from "@/api/client";
import { useCredentials, useDeleteTarget, useProject, usePublications } from "@/api/queries";
import { LanguageMark } from "@/components/LanguageDot";
import { PublicationStatus, languageLabel, registryLabel } from "@/api/types";
import type { SdkTarget } from "@/api/types";
import { timeAgo } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * The one thing that tells two rows apart at a glance, since every other cell is words.
 * Brand hues rather than theme tokens: they carry the same meaning in either theme, and a
 * palette drawn from the theme would give six languages six shades of the same colour.
 */
type Filter = "all" | "automatic" | "manual";

/**
 * The same icons the publishing column uses, so a filter and what it selects look alike —
 * and, once selected, the same colour too. Unselected they stay monochrome: coloured icons
 * competing at once is what the segmented control was meant to quieten.
 */
const filters: {
  id: Filter;
  label: string;
  Icon: typeof Layers;
  tone: string;
  matches: (t: SdkTarget) => boolean;
}[] = [
  { id: "all", label: "All", Icon: Layers, tone: "text-[var(--filter-all)]", matches: () => true },
  {
    id: "automatic",
    label: "Automatic",
    Icon: Zap,
    tone: "text-[var(--code-warning)]",
    matches: (t) => t.autoPublish,
  },
  {
    id: "manual",
    label: "Manual",
    Icon: Hand,
    tone: "text-[var(--success)]",
    matches: (t) => !t.autoPublish,
  },
];

/** One SDK per language and registry, and what each has published. */
export function ProjectTargetsPage() {
  const { orgId: orgSlug = "", projectId: projectSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const projectId = useProjectId(orgId, projectSlug);
  const project = useProject(orgId, projectId);
  const publications = usePublications(orgId, projectId);
  const credentials = useCredentials(orgId);
  const deleteTarget = useDeleteTarget(orgId, projectId);

  const [targetOpen, setTargetOpen] = useState(false);
  const [editing, setEditing] = useState<SdkTarget | undefined>(undefined);
  // Bumped on every opening and used as the dialog's key: the form takes its values from the
  // row it was opened on, and a remount is what makes it read them again.
  const [opening, setOpening] = useState(0);
  const [targetToDelete, setTargetToDelete] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("all");

  const targets = useMemo(() => project.data?.targets ?? [], [project.data]);

  /**
   * How many versions each target has out, and where they can be read. The target row knows
   * its last version but not that it is one of several, nor how to reach it — and a package
   * name that cannot be followed is half an answer.
   */
  const releases = useMemo(() => {
    const map = new Map<string, { count: number; url: string | null }>();

    for (const publication of publications.data ?? []) {
      if (
        publication.status !== PublicationStatus.Succeeded &&
        publication.status !== PublicationStatus.AlreadyExists
      ) {
        continue;
      }

      const existing = map.get(publication.sdkTargetId) ?? { count: 0, url: null };
      map.set(publication.sdkTargetId, {
        count: existing.count + 1,
        url: existing.url ?? publication.registryUrl,
      });
    }

    return map;
  }, [publications.data]);

  const visible = useMemo(
    () => targets.filter(filters.find((f) => f.id === filter)!.matches),
    [targets, filter],
  );

  const deleting = targets.find((t) => t.id === targetToDelete);

  function openDialog(target?: SdkTarget) {
    setEditing(target);
    setOpening((n) => n + 1);
    setTargetOpen(true);
  }

  return (
    <div className="space-y-6">
      {/* hasData: adding, editing and deleting a target all invalidate this query, so a
          refetch that fails would otherwise replace a table the browser still holds with a
          full-width error. */}
      <QueryBoundary
        isLoading={project.isLoading}
        error={project.error}
        hasData={project.data !== undefined}
      >
        {targets.length ? (
          <div className="space-y-3">
            {/* Beside the table, not inside its frame: a toolbar band directly above the
                table's own header row puts two banded strips back to back, which reads as a
                table inside a table. */}
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="outline" onClick={() => openDialog()}>
                <Plus className="size-4" aria-hidden />
                Add target
              </Button>

              {/* The runs live at the organization now, so the way back to "what did these
                  targets actually produce" has to be offered from here rather than assumed
                  from a tab that used to sit beside this one. */}
              <Button
                variant="outline"
                nativeButton={false}
                render={<Link to={`/orgs/${orgSlug}/runs?project=${projectSlug}`} />}
              >
                <History className="size-4" aria-hidden />
                Runs for this project
              </Button>

              {/* One control rather than loose buttons: a segmented set says the choices are
                  exclusive before anything is clicked. */}
              <div className="ml-auto flex items-center rounded-lg border bg-card p-0.5">
                {filters.map(({ id, label, Icon, tone, matches }) => {
                  const count = targets.filter(matches).length;

                  return (
                    <button
                      key={id}
                      type="button"
                      aria-pressed={filter === id}
                      disabled={count === 0 && id !== "all"}
                      onClick={() => setFilter(id)}
                      className={cn(
                        // font-medium on every one, not just the selected: a weight that
                        // changes on click is a width that changes on click, and each
                        // selection nudged the buttons beside it. Background and colour say
                        // which is active without moving anything.
                        "flex cursor-pointer items-center gap-1.5 rounded-[calc(var(--radius)-2px)] px-2.5 py-1 text-xs font-medium transition-colors",
                        "disabled:cursor-not-allowed disabled:opacity-40",
                        filter === id
                          ? "bg-secondary text-secondary-foreground"
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
                      <TableHead>SDK</TableHead>
                      <TableHead className="w-32">Registry</TableHead>
                      <TableHead className="w-40">Publishing</TableHead>
                      <TableHead className="w-48">Publishes to</TableHead>
                      <TableHead className="w-48">Latest release</TableHead>
                      <TableHead className="w-24 text-right">
                        <span className="sr-only">Actions</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {visible.map((target) => {
                      const release = releases.get(target.id);

                      return (
                        <TableRow key={target.id} className="group">
                          {/* Language and package name together: they name one thing, and as
                              two columns the eye had to join them back up on every row. */}
                          <TableCell>
                            {/* The dot sits in a media chip, and the package name beside it
                                rather than under it: the chip gives that alignment
                                structurally, where the pl-4 it replaces only guessed at it. */}
                            <div className="flex items-center gap-3">
                              <LanguageMark language={target.language} />
                              <div className="flex min-w-0 flex-col">
                                {/* The obvious thing to click on a row that has settings, and
                                    the one part of it that did nothing. Underlined on row hover
                                    rather than its own: the pointer is somewhere on the row long
                                    before it reaches ten characters of label. */}
                                <button
                                  type="button"
                                  onClick={() => openDialog(target)}
                                  className="cursor-pointer rounded-sm text-left font-medium group-hover:underline focus-visible:ring-[3px] focus-visible:ring-ring focus-visible:outline-none"
                                >
                                  {languageLabel[target.language]}
                                </button>
                                <span className="truncate font-mono text-xs text-muted-foreground">
                                  {target.packageName}
                                </span>
                              </div>
                            </div>
                          </TableCell>

                          <TableCell>
                            <Badge variant="outline" className="font-mono text-[10px]">
                              {registryLabel[target.registry]}
                            </Badge>
                          </TableCell>

                          {/* "On"/"Off" left the reader to guess what was on. Naming the two
                              modes says what happens when a run succeeds. */}
                          <TableCell>
                            {target.autoPublish ? (
                              <span className="flex items-center gap-1.5 text-xs">
                                <Zap className="size-3.5 text-[var(--code-warning)]" aria-hidden />
                                Automatic
                              </span>
                            ) : (
                              // Coloured like its opposite, and no longer muted: the two are
                              // equal states of the same setting, and greying one of them
                              // read as the target being switched off.
                              <span className="flex items-center gap-1.5 text-xs">
                                <Hand className="size-3.5 text-[var(--success)]" aria-hidden />
                                Manual
                              </span>
                            )}
                          </TableCell>

                          {/* The destination, resolved the way the publish step resolves it:
                              a named credential, or the organization's only usable one. */}
                          <TableCell className="text-xs">
                            {(() => {
                              if (!target.registryCredentialId) {
                                // The words the picker that sets this uses. "not published"
                                // read as an empty history — which is the very next column,
                                // and answers "Nothing published yet" on its own.
                                return (
                                  <span className="text-muted-foreground">Do not publish</span>
                                );
                              }

                              const named = (credentials.data ?? [])
                                .filter((c) => !c.isRevoked)
                                .find((c) => c.id === target.registryCredentialId);

                              if (!named) {
                                return (
                                  <span className="text-[var(--code-warning)]">credential revoked</span>
                                );
                              }

                              // Name over address, like the picker that chose it: the label is
                              // what someone recognises, the URL is what they check. Capped so
                              // a long registry path cannot widen the whole table.
                              return (
                                <div className="max-w-[16rem]">
                                  <p className="truncate">{named.label}</p>
                                  <Tooltip>
                                    <TooltipTrigger
                                      render={
                                        <p className="truncate font-mono text-xs text-muted-foreground" />
                                      }
                                    >
                                      {named.registryUrl}
                                    </TooltipTrigger>
                                    {/* break-all: see CredentialsPage — an unbroken URL
                                        cannot wrap, so the tooltip would clip the very
                                        address it was opened to reveal. */}
                                    <TooltipContent className="break-all">
                                      {named.registryUrl}
                                    </TooltipContent>
                                  </Tooltip>
                                </div>
                              );
                            })()}
                          </TableCell>

                          <TableCell>
                            {target.lastPublishedVersion ? (
                              <>
                                <p className="font-mono text-xs">{target.lastPublishedVersion}</p>
                                <p className="text-xs text-muted-foreground">
                                  {/* The exact moment behind "3 days ago", on the words that
                                      abbreviate it rather than on the whole line. */}
                                  {target.lastPublishedAt ? (
                                    <Tooltip>
                                      <TooltipTrigger render={<span />}>
                                        {timeAgo(target.lastPublishedAt)}
                                      </TooltipTrigger>
                                      <TooltipContent>
                                        {new Date(target.lastPublishedAt).toLocaleString()}
                                      </TooltipContent>
                                    </Tooltip>
                                  ) : (
                                    "—"
                                  )}
                                  {release && release.count > 1 && (
                                    <> · {release.count} releases</>
                                  )}
                                </p>
                              </>
                            ) : (
                              <span className="text-xs text-muted-foreground">
                                Nothing published yet
                              </span>
                            )}
                          </TableCell>

                          <TableCell className="text-right">
                            <div className="flex justify-end gap-1">
                              {/* Kept in the open: it is the one action that leaves this page,
                                  and a destination is worth showing rather than hiding. */}
                              {release?.url && (
                                <Button
                                  size="icon-sm"
                                  variant="ghost"
                                  aria-label={`Open ${target.packageName} on ${registryLabel[target.registry]}`}
                                  title={`Open on ${registryLabel[target.registry]}`}
                                  // Renders an anchor, so Base UI must not assume a button.
                                  nativeButton={false}
                                  render={
                                    <a
                                      href={release.url}
                                      target="_blank"
                                      rel="noreferrer noopener"
                                    />
                                  }
                                >
                                  <ExternalLink className="size-4" aria-hidden />
                                </Button>
                              )}

                              <DropdownMenu>
                                <DropdownMenuTrigger
                                  render={<Button size="icon-sm" variant="ghost" />}
                                  aria-label={`More actions for ${target.packageName}`}
                                >
                                  <MoreHorizontal className="size-4" aria-hidden />
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end" className="w-44">
                                  <DropdownMenuItem
                                    onClick={() => openDialog(target)}
                                  >
                                    <Pencil className="size-4" aria-hidden />
                                    Edit
                                  </DropdownMenuItem>
                                  <DropdownMenuSeparator />
                                  <DropdownMenuItem
                                    variant="destructive"
                                    onClick={() => {
                                      setDeleteError(null);
                                      setTargetToDelete(target.id);
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
                    No target matches this filter.
                  </p>
                )}
              </CardContent>
            </Card>
          </div>
        ) : (
          <EmptyState
            icon={<Package aria-hidden />}
            title="No SDK targets"
            description="A target says which language to emit and which package name to publish it under."
            action={
              <Button variant="outline" onClick={() => openDialog()}>
                <Plus className="size-4" aria-hidden />
                Add target
              </Button>
            }
          />
        )}
      </QueryBoundary>

      <TargetDialog
        key={opening}
        orgId={orgId}
        projectId={projectId}
        target={editing}
        open={targetOpen}
        onOpenChange={setTargetOpen}
      />

      <ConfirmDeleteDialog
        open={targetToDelete !== null}
        onOpenChange={(open) => {
          if (!open) {
            setTargetToDelete(null);
            setDeleteError(null);
          }
        }}
        title="Delete this SDK target?"
        description="The target and the runs that produced it are removed."
        confirmText={deleting?.packageName}
        consequences={["Runs for this target, with their artifacts and logs"]}
        reassurances={["Packages already published stay in their registries"]}
        isPending={deleteTarget.isPending}
        error={deleteError}
        onConfirm={() =>
          targetToDelete &&
          deleteTarget.mutate(
            { targetId: targetToDelete, packageName: deleting?.packageName ?? "" },
            {
              onSuccess: () => {
                toast.success("SDK target deleted.");
                setTargetToDelete(null);
              },
              onError: (error) =>
                setDeleteError(
                  error instanceof ApiError ? error.detail : "Could not delete the target.",
                ),
            },
          )
        }
      />
    </div>
  );
}
