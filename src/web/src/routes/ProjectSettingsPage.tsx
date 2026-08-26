import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { PageBreadcrumb } from "@/components/PageBreadcrumb";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { ApiError } from "@/api/client";
import {
  useDeleteProject, useOrganization, useProject, useRenameProject,
} from "@/api/queries";

const schema = z.object({
  name: z.string().min(1, "A name is required.").max(100, "Use 100 characters or fewer."),
});

export function ProjectSettingsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const navigate = useNavigate();
  const organization = useOrganization(orgId);
  const project = useProject(orgId, projectId);
  const rename = useRenameProject(orgId, projectId);
  const remove = useDeleteProject(orgId);

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: "" },
  });

  useEffect(() => {
    if (project.data) {
      form.reset({ name: project.data.name });
    }
  }, [project.data, form]);

  const onSubmit = form.handleSubmit((values) =>
    rename.mutate(values.name, {
      onSuccess: () => toast.success("Project renamed."),
      onError: (error) =>
        toast.error(error instanceof ApiError ? error.detail : "Could not rename the project."),
    }),
  );

  return (
    <div className="space-y-6">
      <PageBreadcrumb
        crumbs={[
          { label: "Organizations", to: "/" },
          { label: organization.data?.name ?? "Organization", to: `/orgs/${orgId}` },
          { label: project.data?.name ?? "Project", to: `/orgs/${orgId}/projects/${projectId}` },
          { label: "Settings" },
        ]}
      />

      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Project settings</h1>
        <p className="text-sm text-muted-foreground">Naming and removal for this project.</p>
      </div>

      {project.isPending ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <>
          <SettingsSection title="General" description="How this project is named.">
            <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
              <div className="space-y-2">
                <Label htmlFor="name">Name</Label>
                <Input id="name" {...form.register("name")} />
                {form.formState.errors.name && (
                  <p className="text-sm text-destructive">{form.formState.errors.name.message}</p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="slug">Slug</Label>
                <Input id="slug" value={project.data?.slug ?? ""} readOnly disabled />
                <p className="text-xs text-muted-foreground">
                  Fixed once created, so links and scripts that use it keep working.
                </p>
              </div>

              <Button type="submit" disabled={rename.isPending || !form.formState.isDirty}>
                {rename.isPending ? "Saving…" : "Save changes"}
              </Button>
            </form>
          </SettingsSection>

          <DangerSection
            title="Delete project"
            description="Removes the project and everything recorded under it."
          >
            <Button variant="destructive" onClick={() => setDeleteOpen(true)}>
              <Trash2 className="size-4" aria-hidden />
              Delete project
            </Button>
          </DangerSection>
        </>
      )}

      <ConfirmDeleteDialog
        open={deleteOpen}
        onOpenChange={(open) => {
          setDeleteOpen(open);
          if (!open) setDeleteError(null);
        }}
        title={`Delete ${project.data?.name ?? "this project"}?`}
        description="This removes the project and everything recorded under it."
        consequences={[
          `${project.data?.specVersionCount ?? 0} uploaded spec version(s)`,
          `${project.data?.targets.length ?? 0} SDK target(s)`,
          "Its entire run history, with artifacts and logs",
          "Packages already published stay in their registries",
        ]}
        isPending={remove.isPending}
        error={deleteError}
        onConfirm={() =>
          remove.mutate(projectId, {
            onSuccess: () => {
              toast.success("Project deleted.");
              navigate(`/orgs/${orgId}`);
            },
            onError: (error) =>
              setDeleteError(
                error instanceof ApiError ? error.detail : "Could not delete the project.",
              ),
          })
        }
      />
    </div>
  );
}
