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
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { NAME_MAX_LENGTH, NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { ApiError } from "@/api/client";
import {
  useDeleteProject, useProject, useRenameProject,
} from "@/api/queries";

const schema = z.object({
  name: z
    .string()
    .max(NAME_MAX_LENGTH, `Use ${NAME_MAX_LENGTH} characters or fewer.`)
    .refine(isValidResourceName, NAME_RULE),
});

export function ProjectSettingsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const navigate = useNavigate();
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

  const currentSlug = project.data?.slug ?? "";
  const nextSlug = slugPreview(form.watch("name") || "") || currentSlug;
  const slugWillChange = Boolean(currentSlug) && nextSlug !== currentSlug;

  const onSubmit = form.handleSubmit((values) =>
    rename.mutate(values.name, {
      onSuccess: (updated) => {
        toast.success("Project renamed.");
        if (updated.slug !== projectId) {
          navigate(`/orgs/${orgId}/projects/${updated.slug}/settings`, { replace: true });
        }
      },
      onError: (error) =>
        toast.error(error instanceof ApiError ? error.detail : "Could not rename the project."),
    }),
  );

  return (
    <div className="space-y-6">
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
                <Label htmlFor="slug">Address</Label>
                <Input id="slug" value={nextSlug} readOnly disabled className="font-mono text-sm" />
                {slugWillChange ? (
                  // Renaming moves the slug, and the slug is the URL. Saying so before the
                  // click is the difference between a rename and a broken link someone
                  // discovers later.
                  <p className="text-xs text-destructive">
                    Renaming changes this address. Existing links to{" "}
                    <code className="font-mono">{currentSlug}</code> will stop working.
                  </p>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Derived from the name, and used in this project&apos;s URL.
                  </p>
                )}
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
        confirmText={project.data?.name}
        consequences={[
          `${project.data?.specVersionCount ?? 0} uploaded spec version(s)`,
          `${project.data?.targets.length ?? 0} SDK target(s)`,
          "Its entire run history, with artifacts and logs",
          "Packages already published stay in their registries",
        ]}
        isPending={remove.isPending}
        error={deleteError}
        onConfirm={() =>
          project.data &&
          remove.mutate({ projectId, name: project.data.name }, {
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
