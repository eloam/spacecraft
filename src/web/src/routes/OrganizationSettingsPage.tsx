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

      {organization.isPending ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <>
          <SettingsSection title="General" description="How this organization is named.">
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
                    Derived from the name, and used in this organization&apos;s URL.
                  </p>
                )}
              </div>

              <Button type="submit" disabled={rename.isPending || !form.formState.isDirty}>
                {rename.isPending ? "Saving…" : "Save changes"}
              </Button>
            </form>
          </SettingsSection>

          <DangerSection
            title="Delete organization"
            description="Removes every project, run, credential and publication record it owns."
          >
            <Button variant="destructive" onClick={() => setDeleteOpen(true)}>
              <Trash2 className="size-4" aria-hidden />
              Delete organization
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
        title={`Delete ${organization.data?.name ?? "this organization"}?`}
        description="Everything this organization owns goes with it. This cannot be undone."
        confirmText={organization.data?.name}
        consequences={[
          `${projects.data?.length ?? 0} project(s), with their specs and run history`,
          "Registry credentials stored for this organization",
          "The record of what it has published",
          "Packages already published stay in their registries",
        ]}
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
    </div>
  );
}
