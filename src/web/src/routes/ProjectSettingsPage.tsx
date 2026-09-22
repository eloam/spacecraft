import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { FolderGit2, ShieldCheck, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Field, FieldContent, FieldDescription, FieldError, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import { Switch } from "@/components/ui/switch";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { NAME_MAX_LENGTH, NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { describeError } from "@/api/client";
import {
  useDeleteProject, useProject, useRenameProject, useSetStrictValidation,
} from "@/api/queries";

const schema = z.object({
  name: z
    .string()
    .trim()
    .max(NAME_MAX_LENGTH, `Use ${NAME_MAX_LENGTH} characters or fewer.`)
    .refine(isValidResourceName, NAME_RULE),
});

export function ProjectSettingsPage() {
  const { orgId: orgSlug = "", projectId: projectSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const projectId = useProjectId(orgId, projectSlug);
  const navigate = useNavigate();
  const project = useProject(orgId, projectId);
  const rename = useRenameProject(orgId, projectId);
  const remove = useDeleteProject(orgId);
  const strict = useSetStrictValidation(orgId, projectId);

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: "" },
    // The name arrives after the first render: `values` re-syncs it into the form without an
    // effect, and keepDirtyValues keeps a name being typed from being overwritten by a refetch.
    values: project.data ? { name: project.data.name } : undefined,
    resetOptions: { keepDirtyValues: true },
  });

  const name = useWatch({ control: form.control, name: "name" });
  const currentSlug = project.data?.slug ?? "";
  const nextSlug = slugPreview(name || "") || currentSlug;
  const slugWillChange = Boolean(currentSlug) && nextSlug !== currentSlug;

  const onSubmit = form.handleSubmit((values) =>
    rename.mutate(values.name, {
      onSuccess: (updated) => {
        // The saved name is the new baseline, so isDirty is judged against what the server
        // holds. `reset` merges resetOptions and the field just saved is the dirty one, so
        // keepDirtyValues is switched off here: the saved value replaces what was typed and
        // the field is clean again for the next server sync.
        form.reset({ name: updated.name }, { keepDirtyValues: false });
        toast.success("Project renamed.");
        // The slug moved with the name, so the address bar is now pointing at a slug that
        // no longer resolves. Replace rather than push: the old URL is not somewhere to
        // go back to.
        if (updated.slug !== projectSlug) {
          navigate(`/orgs/${orgSlug}/projects/${updated.slug}/settings`, { replace: true });
        }
      },
      onError: (error) => toast.error(describeError(error, "Could not rename the project.")),
    }),
  );

  return (
    <div className="space-y-6">
      {/* Everything below is the project's own settings, so none of it may render without
          the project: an empty Name, a Strict mode switch asserting a default it never read,
          and a delete dialog whose typed-name gate is undefined are all worse than saying
          the load failed. hasData is what keeps a failed refetch — renaming and the strict
          toggle both invalidate this query — from blanking a page the user is working in.
          isPending rather than isLoading, because isLoading is `isPending && isFetching`:
          a first fetch paused for an offline browser leaves it false with no data and no
          error, and the fall-through would be the empty form this guard exists to stop. */}
      <QueryBoundary
        isLoading={project.isPending}
        error={project.error}
        hasData={project.data !== undefined}
        rows={3}
      >
        {project.data && (
          <>
            <SettingsSection
              icon={<FolderGit2 />}
              title="General"
              description="How this project is named."
            >
              <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
                <FieldGroup>
                  <Field data-invalid={!!form.formState.errors.name}>
                    <FieldLabel htmlFor="name">Name</FieldLabel>
                    <Input
                      id="name"
                      aria-invalid={!!form.formState.errors.name}
                      {...form.register("name")}
                    />
                    <FieldError errors={[form.formState.errors.name]} />
                  </Field>

                  <Field>
                    <FieldLabel htmlFor="slug">Slug</FieldLabel>
                    <Input
                      id="slug"
                      value={nextSlug}
                      readOnly
                      disabled
                      className="font-mono text-sm"
                    />
                    {slugWillChange ? (
                      // Renaming moves the slug, and the slug is the URL. Saying so before the
                      // click is the difference between a rename and a broken link someone
                      // discovers later.
                      <FieldDescription className="text-xs text-destructive">
                        Renaming changes the slug. Existing links to{" "}
                        <code className="font-mono">{currentSlug}</code> will stop working.
                      </FieldDescription>
                    ) : (
                      <FieldDescription className="text-xs">
                        Derived from the name, and used in this project&apos;s URL.
                      </FieldDescription>
                    )}
                  </Field>
                </FieldGroup>

                <Button type="submit" disabled={rename.isPending || !form.formState.isDirty}>
                  {rename.isPending ? "Saving…" : "Save changes"}
                </Button>
              </form>
            </SettingsSection>

            <SettingsSection
              icon={<ShieldCheck />}
              title="Validation"
              description="What an uploaded document has to satisfy before an SDK is generated from it."
            >
              <Field orientation="horizontal" className="justify-between gap-6">
                {/* Content first, switch last: the row reads label-then-state, the way it did
                    when a justify-between div held the two apart. */}
                <FieldContent className="max-w-lg">
                  <FieldLabel htmlFor="strict">
                    Strict mode
                  </FieldLabel>
                  <FieldDescription>
                    Beyond &ldquo;is this valid OpenAPI&rdquo;, check whether a usable SDK can
                    come out of it: a base URL to call, method names that can be spelled,
                    described responses, publishable metadata. A document that would produce a
                    broken client is rejected instead of being built.
                  </FieldDescription>
                  <FieldDescription>
                    Turn it off for a specification you consume but do not own, where the
                    findings are noise standing between you and a client that works.
                  </FieldDescription>
                </FieldContent>

                {/* While the change is in flight the switch shows what was asked for; once it
                    lands, the hook writes the response to the cache. Bound to the cache
                    alone, the switch snapped back the moment it was flipped. */}
                <Switch
                  id="strict"
                  checked={strict.isPending ? strict.variables : project.data.strictValidation}
                  disabled={strict.isPending}
                  onCheckedChange={(enabled) =>
                    strict.mutate(enabled, {
                      // Existing versions keep the findings they were given at upload, so the
                      // change is not visible until each is revalidated. Saying so here beats
                      // letting it be discovered as an inconsistency.
                      onSuccess: () =>
                        toast.success(
                          enabled
                            ? "Strict mode on. Revalidate a spec to apply it to what is already uploaded."
                            : "Strict mode off. Revalidate a spec to apply it to what is already uploaded.",
                        ),
                      onError: (error) =>
                        toast.error(describeError(error, "Could not change the setting.")),
                    })
                  }
                />
              </Field>
            </SettingsSection>

            <DangerSection
              icon={<Trash2 />}
              title="Delete project"
              description="Removes the project and everything recorded under it."
            >
              {/* self-start: CardContent is a flex column, which stretches its children,
                  and a delete button as wide as the card reads as the section itself. */}
              <Button
                variant="destructive"
                className="self-start"
                onClick={() => setDeleteOpen(true)}
              >
                <Trash2 className="size-4" aria-hidden />
                Delete project
              </Button>
            </DangerSection>

            <ConfirmDeleteDialog
              open={deleteOpen}
              onOpenChange={setDeleteOpen}
              onOpenChangeComplete={(open) => {
                if (!open) setDeleteError(null);
              }}
              title={`Delete ${project.data.name}?`}
              description="This removes the project and everything recorded under it."
              confirmText={project.data.name}
              consequences={[
                `${project.data.specVersionCount} uploaded spec version(s)`,
                `${project.data.targets.length} SDK target(s)`,
                "Its entire run history, with artifacts and logs",
              ]}
              reassurances={["Packages already published stay in their registries"]}
              isPending={remove.isPending}
              error={deleteError}
              onConfirm={() =>
                remove.mutate({ projectId, name: project.data.name }, {
                  onSuccess: () => {
                    toast.success("Project deleted.");
                    navigate(`/orgs/${orgSlug}`);
                  },
                  onError: (error) =>
                    setDeleteError(describeError(error, "Could not delete the project.")),
                })
              }
            />
          </>
        )}
      </QueryBoundary>
    </div>
  );
}
