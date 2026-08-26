import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { FolderGit2, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
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
import {
  useCreateProject, useDeleteOrganization, useOrganization, useProjects,
} from "@/api/queries";
import { languageLabel } from "@/api/types";
import { PageBreadcrumb } from "@/components/PageBreadcrumb";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

export function ProjectsPage() {
  const { orgId = "" } = useParams();
  const organization = useOrganization(orgId);
  const navigate = useNavigate();
  const deleteOrganization = useDeleteOrganization();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const { data, isLoading, error } = useProjects(orgId);
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      <PageBreadcrumb
        crumbs={[
          { label: "Organizations", to: "/" },
          { label: organization.data?.name ?? "Organization" },
        ]}
      />

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {organization.data?.name ?? "Projects"}
          </h1>
          <p className="text-sm text-muted-foreground">
            One project per API. Each keeps its spec history and the SDKs generated from it.
          </p>
        </div>
        <Button onClick={() => setOpen(true)}>
          <Plus className="size-4" aria-hidden />
          New project
        </Button>
      </div>

      <QueryBoundary isLoading={isLoading} error={error}>
        {data?.length ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((project) => (
              <Link key={project.id} to={`/orgs/${orgId}/projects/${project.id}`} className="group">
                <Card className="h-full transition-colors group-hover:border-foreground/20">
                  <CardHeader>
                    <CardTitle className="truncate">{project.name}</CardTitle>
                    <CardDescription className="font-mono text-xs">{project.slug}</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    <p className="text-sm text-muted-foreground">
                      {project.specVersionCount}{" "}
                      {project.specVersionCount === 1 ? "spec version" : "spec versions"}
                    </p>
                    {project.targets.length > 0 && (
                      <div className="flex flex-wrap gap-1.5">
                        {project.targets.map((target) => (
                          <Badge key={target.id} variant="outline">
                            {languageLabel[target.language]}
                          </Badge>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </Link>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={<FolderGit2 className="size-8" />}
            title="No projects yet"
            description="Create a project, then upload the OpenAPI document describing its API."
            action={
              <Button onClick={() => setOpen(true)}>
                <Plus className="size-4" aria-hidden />
                New project
              </Button>
            }
          />
        )}
      </QueryBoundary>

      <div className="mt-10 rounded-xl border border-destructive/40 p-4">
        <div className="flex items-center justify-between gap-4">
          <div>
            <h2 className="text-sm font-medium text-destructive">Delete organization</h2>
            <p className="text-sm text-muted-foreground">
              Removes every project, run, credential and publication record it owns.
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={() => setDeleteOpen(true)}>
            <Trash2 className="size-4" aria-hidden />
            Delete
          </Button>
        </div>
      </div>

      <ConfirmDeleteDialog
        open={deleteOpen}
        onOpenChange={(open) => {
          setDeleteOpen(open);
          if (!open) setDeleteError(null);
        }}
        title={`Delete ${organization.data?.name ?? "this organization"}?`}
        description="Everything this organization owns goes with it. This cannot be undone."
        confirmText={organization.data?.name}
        consequences={[
          `${data?.length ?? 0} project(s), with their specs and run history`,
          "Registry credentials stored for this organization",
          "The record of what it has published",
          "Packages already published stay in their registries",
        ]}
        isPending={deleteOrganization.isPending}
        error={deleteError}
        onConfirm={() =>
          organization.data &&
          deleteOrganization.mutate(
            { orgId, name: organization.data.name },
            {
              onSuccess: () => {
                toast.success("Organization deleted.");
                navigate("/");
              },
              onError: (error) =>
                setDeleteError(
                  error instanceof ApiError ? error.detail : "Could not delete the organization.",
                ),
            },
          )
        }
      />

      <CreateProjectDialog orgId={orgId} open={open} onOpenChange={setOpen} />
    </div>
  );
}

function CreateProjectDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [name, setName] = useState("");
  const create = useCreateProject(orgId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    create.mutate(
      { name },
      {
        onSuccess: (project) => {
          toast.success(`Created ${project.name}`);
          setName("");
          onOpenChange(false);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not create the project."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>New project</DialogTitle>
            <DialogDescription>Name it after the API it describes.</DialogDescription>
          </DialogHeader>

          <div className="space-y-2 py-4">
            <Label htmlFor="project-name">Name</Label>
            <Input
              id="project-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Billing API"
              autoFocus
            />
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!name.trim() || create.isPending}>
              {create.isPending ? "Creating…" : "Create"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
