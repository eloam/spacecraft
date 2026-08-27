import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { FolderGit2, Plus, Settings } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
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
import { useCreateProject, useProjects } from "@/api/queries";
import { languageLabel } from "@/api/types";
import { NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

export function ProjectsPage() {
  const { orgId = "" } = useParams();
  const { data, isLoading, error } = useProjects(orgId);
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
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
              <Card
                key={project.id}
                className="relative h-full transition-colors hover:border-foreground/20"
              >
                <Link
                  to={`/orgs/${orgId}/projects/${project.slug}`}
                  className="absolute inset-0"
                  aria-label={project.name}
                />
                <CardHeader>
                  <CardTitle className="truncate">{project.name}</CardTitle>
                  <CardDescription className="font-mono text-xs">{project.slug}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <p className="text-sm text-muted-foreground">
                      {project.specVersionCount}{" "}
                      {project.specVersionCount === 1 ? "spec version" : "spec versions"}
                    </p>
                    <Link
                      to={`/orgs/${orgId}/projects/${project.slug}/settings`}
                      // Above the card-wide link, so it wins the click.
                      className={`relative z-10 ${buttonVariants({ variant: "ghost", size: "sm" })}`}
                    >
                      <Settings className="size-4" aria-hidden />
                      <span className="sr-only">{project.name} settings</span>
                    </Link>
                  </div>
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
  // Offered here rather than only in settings: it decides what the first upload is told, and
  // the first upload usually happens before anyone opens the settings page.
  const [strictValidation, setStrictValidation] = useState(true);
  const create = useCreateProject(orgId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    create.mutate(
      { name, strictValidation },
      {
        onSuccess: (project) => {
          toast.success(`Created ${project.name}`);
          setName("");
          setStrictValidation(true);
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

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="project-name">Name</Label>
              <Input
                id="project-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Billing API"
                autoFocus
              />
              {name.trim() && !isValidResourceName(name) && (
                <p className="text-sm text-destructive">{NAME_RULE}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="project-slug">Address</Label>
              <Input
                id="project-slug"
                value={slugPreview(name)}
                readOnly
                disabled
                placeholder="billing-api"
                className="font-mono text-sm"
              />
              <p className="text-xs text-muted-foreground">
                Derived from the name, and unique within this organization.
              </p>
            </div>

            <div className="flex items-start justify-between gap-4 rounded-lg border p-3">
              <div className="space-y-1">
                <Label htmlFor="project-strict" className="text-sm font-medium">
                  Strict validation
                </Label>
                <p className="text-xs text-muted-foreground">
                  Check that a usable SDK can come out of an uploaded document — a base URL to
                  call, method names that can be spelled, described responses — and refuse the
                  ones that would produce a broken client. Turn it off for a specification you
                  consume but do not own.
                </p>
              </div>

              <Switch
                id="project-strict"
                checked={strictValidation}
                onCheckedChange={setStrictValidation}
              />
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!isValidResourceName(name) || create.isPending}>
              {create.isPending ? "Creating…" : "Create"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
