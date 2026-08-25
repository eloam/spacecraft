import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Package, Play, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
import { useAddTarget, useCreateRun, useProject, useRuns, useSpecs } from "@/api/queries";
import {
  SdkLanguage,
  SpecValidationStatus,
  languageLabel,
  registryLabel,
  stepKindLabel,
} from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

export function ProjectPage() {
  const { orgId = "", projectId = "" } = useParams();
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);
  const runs = useRuns(orgId, projectId);

  const [targetOpen, setTargetOpen] = useState(false);
  const [generateOpen, setGenerateOpen] = useState(false);

  const canGenerate =
    (project.data?.targets.length ?? 0) > 0 &&
    (specs.data?.some((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? false);

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <Link to={`/orgs/${orgId}`} className="text-sm text-muted-foreground hover:underline">
            ← Projects
          </Link>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">
            {project.data?.name ?? "Project"}
          </h1>
          <p className="font-mono text-xs text-muted-foreground">{project.data?.slug}</p>
        </div>

        <Button onClick={() => setGenerateOpen(true)} disabled={!canGenerate}>
          <Play className="size-4" aria-hidden />
          Generate SDK
        </Button>
      </div>

      <Tabs defaultValue="specs">
        <TabsList>
          <TabsTrigger value="specs">Specs</TabsTrigger>
          <TabsTrigger value="targets">SDK targets</TabsTrigger>
          <TabsTrigger value="runs">Runs</TabsTrigger>
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
                        <SpecValidationBadge
                          status={spec.validationStatus}
                          warnings={spec.warningCount}
                          errors={spec.errorCount}
                        />
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
                  {runs.data.map((run) => (
                    <TableRow key={run.id}>
                      <TableCell>
                        <Link to={`/orgs/${orgId}/runs/${run.id}`}>
                          <RunStatusBadge status={run.status} />
                        </Link>
                      </TableCell>
                      <TableCell className="font-mono text-xs">{run.resolvedVersion ?? "—"}</TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {run.steps.filter((s) => s.status === 3).length}/{run.steps.length} steps
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {new Date(run.queuedAt).toLocaleString()}
                      </TableCell>
                    </TableRow>
                  ))}
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
        onSuccess: () => {
          toast.success("Run queued");
          onOpenChange(false);
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

            <label className="flex items-start gap-2 text-sm">
              <input
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
