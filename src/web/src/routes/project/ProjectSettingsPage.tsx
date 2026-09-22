import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import { FolderGit2, ShieldCheck, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Field, FieldContent, FieldDescription, FieldLabel } from "@/components/ui/field";
import { Switch } from "@/components/ui/switch";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { RenameResourceForm } from "@/components/RenameResourceForm";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { describeError } from "@/api/client";
import {
  useDeleteProject, useProject, useRenameProject, useSetStrictValidation,
} from "@/api/queries";
import { plural } from "@/lib/format";

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

  return (
    <div className="space-y-6">
      <title>
        {project.data ? `Settings — ${project.data.name} — SpaceCraft` : `Settings — SpaceCraft`}
      </title>

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
              <RenameResourceForm
                resourceKind="project"
                name={project.data.name}
                slug={project.data.slug}
                isPending={rename.isPending}
                onSubmit={async ({ name }) => {
                  const updated = await rename.mutateAsync(name);
                  toast.success("Project renamed.");
                  // The slug moved with the name, so the address bar is now pointing at a
                  // slug that no longer resolves. Replace rather than push: the old URL is
                  // not somewhere to go back to.
                  if (updated.slug !== projectSlug) {
                    navigate(`/orgs/${orgSlug}/projects/${updated.slug}/settings`, {
                      replace: true,
                    });
                  }
                }}
              />
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
                if (!open) remove.reset();
              }}
              title={`Delete ${project.data.name}?`}
              description="This removes the project and everything recorded under it."
              confirmText={project.data.name}
              consequences={[
                plural(project.data.specVersionCount, "uploaded spec version"),
                plural(project.data.targets.length, "SDK target"),
                "Its entire run history, with artifacts and logs",
              ]}
              reassurances={["Packages already published stay in their registries"]}
              isPending={remove.isPending}
              error={remove.error}
              onConfirm={() =>
                remove.mutate({ projectId, name: project.data.name }, {
                  onSuccess: () => {
                    toast.success("Project deleted.");
                    navigate(`/orgs/${orgSlug}`);
                  },
                })
              }
            />
          </>
        )}
      </QueryBoundary>
    </div>
  );
}
