import { useState } from "react";
import { useParams } from "react-router-dom";
import { useOrganizationId } from "@/hooks/useRouteIdentity";
import { FolderGit2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ItemContent, ItemDescription } from "@/components/ui/item";
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
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { LanguageDot } from "@/components/LanguageDot";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { TileLink } from "@/components/TileLink";
import { useCreateProject, useOrganizations, useProjects } from "@/api/queries";
import { languageLabel } from "@/api/types";
import { plural } from "@/lib/format";
import { NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { describeError } from "@/api/client";
import { toast } from "sonner";

export function ProjectsPage() {
  const { orgId: orgSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const organization = useOrganizations().data?.find((o) => o.id === orgId);
  const { data, isLoading, error } = useProjects(orgId);
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      <title>
        {organization ? `Projects — ${organization.name} — SpaceCraft` : `Projects — SpaceCraft`}
      </title>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-medium tracking-tight">Projects</h1>
          <p className="text-sm text-muted-foreground">
            One project per API. Each keeps its spec history and the SDKs generated from it.
          </p>
        </div>
        <Button onClick={() => setOpen(true)}>
          <Plus className="size-4" aria-hidden />
          New project
        </Button>
      </div>

      <QueryBoundary isLoading={isLoading} error={error} hasData={data !== undefined}>
        {data?.length ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((project) => (
              // The count leads as the label because in a list of projects the name is what
              // varies and the slug is what someone copies, so neither wants the quiet slot.
              <TileLink key={project.id} to={`/orgs/${orgSlug}/projects/${project.slug}`}>
                <ItemContent className="gap-1">
                  <ItemDescription className="text-xs font-medium tracking-wider uppercase">
                    {plural(project.specVersionCount, "spec version")}
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
              </TileLink>
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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <CreateProjectForm orgId={orgId} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  );
}

/**
 * The form is a child of the content, which Base UI unmounts once the dialog has closed, so
 * the name and the switch go with the rest of the state on every close — Cancel and Escape
 * included — and each opening starts from the defaults. Held by the dialog component
 * instead, they were only put back after a creation went through, and a cancelled attempt
 * came back pre-filled the next time it opened.
 */
function CreateProjectForm({
  orgId,
  onDone,
}: {
  orgId: string;
  /** Closes the dialog after a creation. */
  onDone: () => void;
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
          onDone();
        },
        onError: (err) => toast.error(describeError(err, "Could not create the project.")),
      },
    );
  }

  return (
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
          <FieldLabel htmlFor="project-slug">Slug</FieldLabel>
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
        <DialogClose render={<Button type="button" variant="outline">Cancel</Button>} />
        <Button type="submit" disabled={!isValidResourceName(name) || create.isPending}>
          {create.isPending ? "Creating…" : "Create"}
        </Button>
      </DialogFooter>
    </form>
  );
}
