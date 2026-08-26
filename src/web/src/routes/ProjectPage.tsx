import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { FileCode2, History, Package, Play, Plus, Settings, Trash2, TriangleAlert, XCircle } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { RunStatusBadge } from "@/components/StatusBadge";
import { SpecUploadCard } from "@/components/SpecUploadCard";
import { useAddTarget, useCreateRun, useOrganization,
  useDeleteTarget,
  useProject, useRuns, useSpecDiagnostics, useSpecs } from "@/api/queries";
import {
  SdkLanguage,
  SpecValidationStatus,
  languageLabel,
  registryLabel,
  stepKindLabel,
  runStatusLabel,
  isTerminal,
  formatRunDuration,
  describeRunProgress,
} from "@/api/types";
import { PageBreadcrumb } from "@/components/PageBreadcrumb";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

export function ProjectPage() {
  const { orgId = "", projectId = "" } = useParams();
  const navigate = useNavigate();
  const organization = useOrganization(orgId);
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);
  const runs = useRuns(orgId, projectId);

  const [targetOpen, setTargetOpen] = useState(false);
  const [diagnosticsFor, setDiagnosticsFor] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [targetToDelete, setTargetToDelete] = useState<string | null>(null);
  const deleteTarget = useDeleteTarget(orgId, projectId);
  const [generateOpen, setGenerateOpen] = useState(false);

  const canGenerate =
    (project.data?.targets.length ?? 0) > 0 &&
    (specs.data?.some((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? false);

  return (
    <div className="space-y-6">
      <PageBreadcrumb
        crumbs={[
          { label: "Organizations", to: "/" },
          { label: organization.data?.name ?? "Organization", to: `/orgs/${orgId}` },
          { label: project.data?.name ?? "Project" },
        ]}
      />

      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {project.data?.name ?? "Project"}
          </h1>
          <p className="font-mono text-xs text-muted-foreground">{project.data?.slug}</p>
        </div>

        <div className="flex items-center gap-2">
          <Link
            to={`/orgs/${orgId}/projects/${projectId}/settings`}
            className={buttonVariants({ variant: "ghost", size: "sm" })}
          >
            <Settings className="size-4" aria-hidden />
            <span className="sr-only">Project settings</span>
          </Link>
          <Button onClick={() => setGenerateOpen(true)} disabled={!canGenerate}>
          <Play className="size-4" aria-hidden />
          Generate SDK
          </Button>
        </div>
      </div>

      <Tabs defaultValue="specs">
        <TabsList>
          <TabsTrigger value="specs">
            <FileCode2 className="size-4" aria-hidden />
            Specs
          </TabsTrigger>
          <TabsTrigger value="targets">
            {/* Package, matching the icon the SDK targets list already uses for a package. */}
            <Package className="size-4" aria-hidden />
            SDK targets
          </TabsTrigger>
          <TabsTrigger value="runs">
            <History className="size-4" aria-hidden />
            Runs
          </TabsTrigger>
        </TabsList>

        <TabsContent value="specs" className="mt-6 space-y-6">
          <SpecUploadCard orgId={orgId} projectId={projectId} />

          <QueryBoundary isLoading={specs.isLoading} error={specs.error}>
            {specs.data?.length ? (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>#</TableHead>
                    <TableHead>API</TableHead>
                    <TableHead>Version</TableHead>
                    <TableHead>Validation</TableHead>
                    <TableHead className="text-right">Uploaded</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {specs.data.map((spec) => (
                    <TableRow key={spec.id}>
                      <TableCell className="font-mono text-xs">{spec.sequence}</TableCell>
                      <TableCell>{spec.apiTitle ?? "—"}</TableCell>
                      <TableCell className="font-mono text-xs">{spec.apiVersion ?? "—"}</TableCell>
                      <TableCell>
                        {spec.warningCount + spec.errorCount > 0 ? (
                          // The count was a dead end: it told you something was wrong
                          // without any way to find out what.
                          <button
                            type="button"
                            onClick={() => setDiagnosticsFor(spec.id)}
                            className="cursor-pointer rounded-md focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
                            aria-label={`Show ${spec.errorCount + spec.warningCount} validation findings`}
                          >
                            <SpecValidationBadge
                              status={spec.validationStatus}
                              warnings={spec.warningCount}
                              errors={spec.errorCount}
                            />
                          </button>
                        ) : (
                          <SpecValidationBadge
                            status={spec.validationStatus}
                            warnings={spec.warningCount}
                            errors={spec.errorCount}
                          />
                        )}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {new Date(spec.createdAt).toLocaleString()}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : null}
          </QueryBoundary>
        </TabsContent>

        <TabsContent value="targets" className="mt-6 space-y-4">
          <div className="flex justify-end">
            <Button variant="outline" onClick={() => setTargetOpen(true)}>
              <Plus className="size-4" aria-hidden />
              Add target
            </Button>
          </div>

          <QueryBoundary isLoading={project.isLoading} error={project.error}>
            {project.data?.targets.length ? (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Language</TableHead>
                    <TableHead>Package</TableHead>
                    <TableHead>Registry</TableHead>
                    <TableHead>Auto-publish</TableHead>
                    <TableHead className="text-right">Last published</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {project.data.targets.map((target) => (
                    <TableRow key={target.id}>
                      <TableCell>{languageLabel[target.language]}</TableCell>
                      <TableCell className="font-mono text-xs">{target.packageName}</TableCell>
                      <TableCell>{registryLabel[target.registry]}</TableCell>
                      <TableCell>
                        <Badge variant={target.autoPublish ? "default" : "secondary"}>
                          {target.autoPublish ? "On" : "Off"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs">
                        {target.lastPublishedVersion ?? "—"}
                      </TableCell>
                      <TableCell>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setTargetToDelete(target.id)}
                        >
                          <Trash2 className="size-4" aria-hidden />
                          <span className="sr-only">Delete {target.packageName}</span>
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : (
              <EmptyState
                icon={<Package className="size-8" />}
                title="No SDK targets"
                description="A target says which language to emit and which package name to publish it under."
                action={
                  <Button variant="outline" onClick={() => setTargetOpen(true)}>
                    <Plus className="size-4" aria-hidden />
                    Add target
                  </Button>
                }
              />
            )}
          </QueryBoundary>
        </TabsContent>

        <TabsContent value="runs" className="mt-6">
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
        </TabsContent>
      </Tabs>

      <AddTargetDialog
        orgId={orgId}
        projectId={projectId}
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
        consequences={[
          "Runs for this target, with their artifacts and logs",
          "Packages already published stay in their registries",
        ]}
        isPending={deleteTarget.isPending}
        error={deleteError}
        onConfirm={() =>
          targetToDelete &&
          deleteTarget.mutate(targetToDelete, {
            onSuccess: () => {
              toast.success("SDK target deleted.");
              setTargetToDelete(null);
            },
            onError: (error) =>
              setDeleteError(
                error instanceof ApiError ? error.detail : "Could not delete the target.",
              ),
          })
        }
      />

      <DiagnosticsDialog
        orgId={orgId}
        projectId={projectId}
        specId={diagnosticsFor}
        onClose={() => setDiagnosticsFor(null)}
      />

      <GenerateDialog
        orgId={orgId}
        projectId={projectId}
        open={generateOpen}
        onOpenChange={setGenerateOpen}
      />
    </div>
  );
}

function SpecValidationBadge({
  status,
  warnings,
  errors,
}: {
  status: number;
  warnings: number;
  errors: number;
}) {
  if (status === SpecValidationStatus.Invalid) {
    return <Badge variant="destructive">{errors} error{errors === 1 ? "" : "s"}</Badge>;
  }
  if (status === SpecValidationStatus.ValidWithWarnings) {
    return <Badge variant="secondary">{warnings} warning{warnings === 1 ? "" : "s"}</Badge>;
  }
  if (status === SpecValidationStatus.Valid) {
    return <Badge variant="outline">Valid</Badge>;
  }
  return <Badge variant="secondary">Pending</Badge>;
}

function AddTargetDialog({
  orgId,
  projectId,
  open,
  onOpenChange,
}: {
  orgId: string;
  projectId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [packageName, setPackageName] = useState("");
  const [autoPublish, setAutoPublish] = useState(false);
  const add = useAddTarget(orgId, projectId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    add.mutate(
      // TypeScript is the only language wired end to end today; the API rejects the rest.
      { language: SdkLanguage.TypeScript, packageName, autoPublish },
      {
        onSuccess: () => {
          toast.success(`Added ${packageName}`);
          setPackageName("");
          onOpenChange(false);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not add the target."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Add an SDK target</DialogTitle>
            <DialogDescription>
              TypeScript publishes to npm. Other languages are modelled but not yet wired up.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="package-name">Package name</Label>
              <Input
                id="package-name"
                value={packageName}
                onChange={(e) => setPackageName(e.target.value)}
                placeholder="@acme/billing-sdk"
                autoFocus
              />
            </div>

            <label className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                checked={autoPublish}
                onChange={(e) => setAutoPublish(e.target.checked)}
                className="mt-0.5"
              />
              <span>
                Publish automatically on every new spec version
                <span className="block text-xs text-muted-foreground">
                  Off by default — publishing to a public registry cannot be undone.
                </span>
              </span>
            </label>
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!packageName.trim() || add.isPending}>
              {add.isPending ? "Adding…" : "Add target"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function GenerateDialog({
  orgId,
  projectId,
  open,
  onOpenChange,
}: {
  orgId: string;
  projectId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const navigate = useNavigate();
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);
  const createRun = useCreateRun(orgId);

  const usableSpecs = specs.data?.filter((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? [];
  const targets = project.data?.targets ?? [];

  const [specId, setSpecId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [publish, setPublish] = useState(false);

  const selectedSpec = specId || usableSpecs[0]?.id || "";
  const selectedTarget = targetId || targets[0]?.id || "";

  function submit(event: React.FormEvent) {
    event.preventDefault();
    createRun.mutate(
      { specVersionId: selectedSpec, sdkTargetId: selectedTarget, publish },
      {
        onSuccess: (run) => {
          onOpenChange(false);
          // Queuing a run is the point of this dialog; leaving the user on the tab they
          // started from means the thing they asked for happens somewhere they cannot see.
          navigate(`/orgs/${orgId}/runs/${run.id}`);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not queue the run."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Generate an SDK</DialogTitle>
            <DialogDescription>
              Runs {Object.values(stepKindLabel).join(" → ").toLowerCase()}.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="spec">Spec version</Label>
              <select
                id="spec"
                value={selectedSpec}
                onChange={(e) => setSpecId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {usableSpecs.map((spec) => (
                  <option key={spec.id} value={spec.id}>
                    #{spec.sequence} — {spec.apiTitle ?? "untitled"} {spec.apiVersion ?? ""}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-2">
              <Label htmlFor="target">Target</Label>
              <select
                id="target"
                value={selectedTarget}
                onChange={(e) => setTargetId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {targets.map((target) => (
                  <option key={target.id} value={target.id}>
                    {languageLabel[target.language]} — {target.packageName}
                  </option>
                ))}
              </select>
            </div>

            <label htmlFor="publish" className="flex items-start gap-2 text-sm">
              <input
                id="publish"
                type="checkbox"
                checked={publish}
                onChange={(e) => setPublish(e.target.checked)}
                className="mt-0.5"
              />
              <span>
                Publish to the registry after a successful build
                <span className="block text-xs text-muted-foreground">
                  Requires a registry credential and cannot be undone once accepted.
                </span>
              </span>
            </label>
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!selectedSpec || !selectedTarget || createRun.isPending}>
              {createRun.isPending ? "Queueing…" : "Queue run"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The findings behind a spec's warning count.
 *
 * A large document produces a lot of them — the Keycloak Admin API yields 413 — so the list
 * is virtualised by nothing more than a scroll container, and only fetched once opened.
 */
function DiagnosticsDialog({
  orgId,
  projectId,
  specId,
  onClose,
}: {
  orgId: string;
  projectId: string;
  specId: string | null;
  onClose: () => void;
}) {
  const diagnostics = useSpecDiagnostics(orgId, projectId, specId);

  return (
    <Dialog open={specId !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Validation findings</DialogTitle>
          <DialogDescription>
            {diagnostics.data
              ? `${diagnostics.data.length} finding${diagnostics.data.length === 1 ? "" : "s"} recorded when this spec was uploaded.`
              : "Loading…"}
          </DialogDescription>
        </DialogHeader>

        <div className="max-h-96 overflow-y-auto rounded-lg border">
          {diagnostics.isPending ? (
            <div className="space-y-2 p-3">
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-4 w-2/3" />
            </div>
          ) : (
            <ul className="divide-y text-sm">
              {diagnostics.data?.map((finding, index) => (
                <li key={index} className="flex gap-2 p-3">
                  {finding.severity === 2 ? (
                    <XCircle className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
                  ) : (
                    <TriangleAlert className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
                  )}
                  <div className="min-w-0">
                    <p>{finding.message}</p>
                    {finding.pointer && (
                      <p className="mt-0.5 font-mono text-xs break-all text-muted-foreground">
                        {finding.pointer}
                      </p>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
