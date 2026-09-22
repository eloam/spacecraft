import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import {
  ExternalLink,
  Hand,
  History,
  Layers,
  MoreHorizontal,
  Package,
  Pencil,
  Plus,
  Trash2,
  Zap,
  type LucideIcon,
} from "lucide-react";
import { toast } from "sonner";
import { Button, buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { EmptyState } from "@/components/EmptyState";
import { FilterToggleGroup } from "@/components/FilterToggleGroup";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { RelativeTime } from "@/components/RelativeTime";
import { TableCard } from "@/components/TableCard";
import { TargetDialog } from "@/routes/project/TargetDialog";
import { useCredentials, useDeleteTarget, useProject, usePublications } from "@/api/queries";
import { LanguageMark } from "@/components/LanguageDot";
import { PublicationStatus, languageLabel, registryLabel, type SdkTarget } from "@/api/types";
import { plural } from "@/lib/format";

/** In the order the strip shows them; the union below is derived from it. */
const FILTER_IDS = ["all", "automatic", "manual"] as const;
type Filter = (typeof FILTER_IDS)[number];

/** The publishing column's own icons and colours for the two modes it filters on. */
const filters: Record<
  Filter,
  { label: string; Icon: LucideIcon; tone: string; matches: (t: SdkTarget) => boolean }
> = {
  all: { label: "All", Icon: Layers, tone: "text-[var(--filter-all)]", matches: () => true },
  automatic: {
    label: "Automatic",
    Icon: Zap,
    tone: "text-[var(--code-warning)]",
    matches: (t) => t.autoPublish,
  },
  manual: {
    label: "Manual",
    Icon: Hand,
    tone: "text-[var(--success)]",
    matches: (t) => !t.autoPublish,
  },
};

/** One SDK per language and registry, and what each has published. */
export function ProjectTargetsPage() {
  const { orgSlug = "", projectSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const projectId = useProjectId(orgId, projectSlug);
  const project = useProject(orgId, projectId);
  const publications = usePublications(orgId, projectId);
  const credentials = useCredentials(orgId);
  const deleteTarget = useDeleteTarget(orgId, projectId);

  const [targetOpen, setTargetOpen] = useState(false);
  const [editing, setEditing] = useState<SdkTarget | undefined>(undefined);
  // The row itself rather than its id: looked up in `targets` on each render, it vanished
  // as soon as the delete succeeded and the project refetched, and the package name the
  // dialog asks to type flipped to nothing while it was still fading out. Kept until
  // `onOpenChangeComplete(false)`, Base UI's after-close-animation callback, so nothing the
  // dialog shows can change while it leaves.
  const [deleting, setDeleting] = useState<SdkTarget | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
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

  const visible = useMemo(() => targets.filter(filters[filter].matches), [targets, filter]);

  function openDialog(target?: SdkTarget) {
    setEditing(target);
    setTargetOpen(true);
  }

  return (
    <div className="space-y-6">
      <title>
        {project.data
          ? `SDK targets — ${project.data.name} — SpaceCraft`
          : `SDK targets — SpaceCraft`}
      </title>

      {/* hasData: adding, editing and deleting a target all invalidate this query, so a
          refetch that fails would otherwise replace a table the browser still holds with a
          full-width error. isPending rather than isLoading: the query is disabled until the
          project id resolves, and isLoading is `isPending && isFetching`, false while
          disabled, so every cold load showed "No SDK targets" before the skeleton. */}
      <QueryBoundary
        isLoading={project.isPending}
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

              <FilterToggleGroup
                aria-label="Filter targets"
                className="ml-auto"
                items={FILTER_IDS.map((id) => {
                  const { label, Icon, tone, matches } = filters[id];
                  const count = targets.filter(matches).length;
                  const disabled = count === 0 && id !== "all";
                  return { value: id, label, Icon, tone, count, disabled };
                })}
                value={[filter]}
                // Pressing the pressed filter again releases it, which here means all.
                onValueChange={(next) => setFilter(next[0] ?? "all")}
              />
            </div>

            <TableCard>
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

                            // "Revoked" is a claim about the list, so it waits for the list:
                            // read off an empty one still on its way, every named target
                            // opened on a warning that a moment later was not there.
                            if (credentials.isPending) {
                              return <Skeleton className="h-4 w-24" />;
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
                                  {/* The trigger is a <button>, so the address a narrow cell
                                      cuts short can be opened from the keyboard, not only by
                                      hovering. */}
                                  <TooltipTrigger className="block max-w-full truncate text-left font-mono text-xs text-muted-foreground">
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
                                  <RelativeTime iso={target.lastPublishedAt} />
                                ) : (
                                  "—"
                                )}
                                {release && release.count > 1 && (
                                  <> · {plural(release.count, "release")}</>
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
                              <a
                                href={release.url}
                                target="_blank"
                                rel="noreferrer noopener"
                                className={buttonVariants({ variant: "ghost", size: "icon-sm" })}
                                aria-label={`Open ${target.packageName} on ${registryLabel[target.registry]} (opens in a new tab)`}
                              >
                                <ExternalLink className="size-4" aria-hidden />
                              </a>
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
                                    setDeleting(target);
                                    setDeleteOpen(true);
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
            </TableCard>
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
        orgId={orgId}
        projectId={projectId}
        target={editing}
        open={targetOpen}
        onOpenChange={setTargetOpen}
      />

      <ConfirmDeleteDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        onOpenChangeComplete={(open) => {
          if (!open) {
            setDeleting(null);
            deleteTarget.reset();
          }
        }}
        title="Delete this SDK target?"
        description="The target and the runs that produced it are removed."
        confirmText={deleting?.packageName}
        consequences={["Runs for this target, with their artifacts and logs"]}
        reassurances={["Packages already published stay in their registries"]}
        isPending={deleteTarget.isPending}
        error={deleteTarget.error}
        onConfirm={() =>
          deleting &&
          deleteTarget.mutate(
            { targetId: deleting.id, packageName: deleting.packageName },
            {
              onSuccess: () => {
                toast.success("SDK target deleted.");
                setDeleteOpen(false);
              },
            },
          )
        }
      />
    </div>
  );
}
