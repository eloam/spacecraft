import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Building2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { NAME_MAX_LENGTH, NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { ApiError } from "@/api/client";
import {
  useDeleteOrganization, useOrganization, useProjects, useRenameOrganization,
} from "@/api/queries";

const schema = z.object({
  name: z
    .string()
    .max(NAME_MAX_LENGTH, `Use ${NAME_MAX_LENGTH} characters or fewer.`)
    .refine(isValidResourceName, NAME_RULE),
});

export function OrganizationSettingsPage() {
  const { orgId = "" } = useParams();
  const navigate = useNavigate();
  const organization = useOrganization(orgId);
  const projects = useProjects(orgId);
  const rename = useRenameOrganization(orgId);
  const remove = useDeleteOrganization();

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: "" },
  });

  // The name arrives after the first render, so seed the field once it does rather than
  // leaving the form permanently empty.
  useEffect(() => {
    if (organization.data) {
      form.reset({ name: organization.data.name });
    }
  }, [organization.data, form]);

  const currentSlug = organization.data?.slug ?? "";
  const nextSlug = slugPreview(form.watch("name") || "") || currentSlug;
  const slugWillChange = Boolean(currentSlug) && nextSlug !== currentSlug;

  const onSubmit = form.handleSubmit((values) =>
    rename.mutate(values.name, {
      onSuccess: (updated) => {
        toast.success("Organization renamed.");
        // The slug moved with the name, so the address bar is now pointing at a slug that
        // no longer resolves. Replace rather than push: the old URL is not somewhere to
        // go back to.
        if (updated.slug !== orgId) {
          navigate(`/orgs/${updated.slug}/settings`, { replace: true });
        }
      },
      onError: (error) =>
        toast.error(error instanceof ApiError ? error.detail : "Could not rename the organization."),
    }),
  );

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm text-muted-foreground">
          Naming and removal for this organization.
        </p>
      </div>

      {/* None of this may render without the organization: the Name field would sit empty,
          the address blank, and the delete dialog's typed-name gate would be undefined —
          which ConfirmDeleteDialog reads as "no gate", enabling a Delete button that then
          does nothing. hasData keeps a failed refetch — renaming invalidates this query —
          from replacing settings the user is working in with a full-page error.
          isPending rather than isLoading, because isLoading is `isPending && isFetching`:
          a first fetch paused for an offline browser leaves it false with no data and no
          error, and the fall-through would be the empty form this guard exists to stop. */}
      <QueryBoundary
        isLoading={organization.isPending}
        error={organization.error}
        hasData={organization.data !== undefined}
        rows={2}
      >
        <SettingsSection
          icon={<Building2 />}
          title="General"
          description="How this organization is named."
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
                <FieldLabel htmlFor="slug">Address</FieldLabel>
                <Input id="slug" value={nextSlug} readOnly disabled className="font-mono text-sm" />
                {slugWillChange ? (
                  // Renaming moves the slug, and the slug is the URL. Saying so before the
                  // click is the difference between a rename and a broken link someone
                  // discovers later.
                  <FieldDescription className="text-xs text-destructive">
                    Renaming changes this address. Existing links to{" "}
                    <code className="font-mono">{currentSlug}</code> will stop working.
                  </FieldDescription>
                ) : (
                  <FieldDescription className="text-xs">
                    Derived from the name, and used in this organization&apos;s URL.
                  </FieldDescription>
                )}
              </Field>
            </FieldGroup>

            <Button type="submit" disabled={rename.isPending || !form.formState.isDirty}>
              {rename.isPending ? "Saving…" : "Save changes"}
            </Button>
          </form>
        </SettingsSection>

        <DangerSection
          icon={<Trash2 />}
          title="Delete organization"
          description="Removes every project, run, credential and publication record it owns."
        >
          <Button variant="destructive" onClick={() => setDeleteOpen(true)}>
            <Trash2 className="size-4" aria-hidden />
            Delete organization
          </Button>
        </DangerSection>

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
            // The project list is its own query, and it can fail while the organization
            // loads. "0 project(s)" would then understate the blast radius of the one
            // delete that takes everything, so count only when there is a count.
            projects.data
              ? `${projects.data.length} project(s), with their specs and run history`
              : "Every project in it, with their specs and run history",
            "Registry credentials stored for this organization",
            "The record of what it has published",
          ]}
          reassurances={["Packages already published stay in their registries"]}
          isPending={remove.isPending}
          error={deleteError}
          onConfirm={() =>
            organization.data &&
            remove.mutate(
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
      </QueryBoundary>
    </div>
  );
}
