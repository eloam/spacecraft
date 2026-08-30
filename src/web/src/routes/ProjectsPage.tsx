import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { FolderGit2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Item, ItemContent, ItemDescription } from "@/components/ui/item";
import { Input } from "@/components/ui/input";
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
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
import { LanguageDot } from "@/components/LanguageDot";
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
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((project) => (
              // The whole tile is the link, the way the organization tiles are. The count
              // leads as the label because in a list of projects the name is what varies
              // and the slug is what someone copies, so neither wants the quiet slot.
              <Item
                key={project.id}
                variant="muted"
                className="h-full flex-col items-stretch border-border"
                render={<Link to={`/orgs/${orgId}/projects/${project.slug}`} />}
              >
                <ItemContent className="gap-1">
                  <ItemDescription className="text-xs font-medium tracking-wider uppercase">
                    {project.specVersionCount}{" "}
                    {project.specVersionCount === 1 ? "spec version" : "spec versions"}
                  </ItemDescription>
                  <span className="truncate font-heading text-lg font-semibold">
                    {project.name}
                  </span>
                  <span className="truncate font-mono text-xs text-muted-foreground">
                    {project.slug}
                  </span>
                </ItemContent>

                {project.targets.length > 0 && (
                  <ItemContent className="flex-none">
                    <div className="flex flex-wrap gap-1.5">
                      {project.targets.map((target) => (
                        <Badge key={target.id} variant="outline" className="gap-1.5">
                          <LanguageDot language={target.language} />
                          {languageLabel[target.language]}
                        </Badge>
                      ))}
                    </div>
                  </ItemContent>
                )}
              </Item>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={<FolderGit2 aria-hidden />}
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
  // Only once something has been typed: an untouched field is not yet wrong.
  const nameInvalid = Boolean(name.trim()) && !isValidResourceName(name);

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

          <FieldGroup className="py-4">
            <Field data-invalid={nameInvalid}>
              <FieldLabel htmlFor="project-name">Name</FieldLabel>
              <Input
                id="project-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Billing API"
                autoFocus
                aria-invalid={nameInvalid}
              />
              <FieldError errors={nameInvalid ? [{ message: NAME_RULE }] : undefined} />
            </Field>

            <Field>
              <FieldLabel htmlFor="project-slug">Address</FieldLabel>
              <Input
                id="project-slug"
                value={slugPreview(name)}
                readOnly
                disabled
                placeholder="billing-api"
                className="font-mono text-sm"
              />
              <FieldDescription>
                Derived from the name, and unique within this organization.
              </FieldDescription>
            </Field>

            <Field orientation="horizontal" className="rounded-lg border p-3">
              <FieldContent>
                <FieldLabel htmlFor="project-strict">Strict validation</FieldLabel>
                <FieldDescription>
                  Check that a usable SDK can come out of an uploaded document — a base URL to
                  call, method names that can be spelled, described responses — and refuse the
                  ones that would produce a broken client. Turn it off for a specification you
                  consume but do not own.
                </FieldDescription>
              </FieldContent>

              <Switch
                id="project-strict"
                checked={strictValidation}
                onCheckedChange={setStrictValidation}
              />
            </Field>
          </FieldGroup>

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
