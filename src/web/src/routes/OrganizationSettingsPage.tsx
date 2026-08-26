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
  useDeleteOrganization, useOrganization, useProjects, useRenameOrganization,
} from "@/api/queries";

const schema = z.object({
  name: z.string().min(1, "A name is required.").max(100, "Use 100 characters or fewer."),
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

  const onSubmit = form.handleSubmit((values) =>
    rename.mutate(values.name, {
      onSuccess: () => toast.success("Organization renamed."),
      onError: (error) =>
        toast.error(error instanceof ApiError ? error.detail : "Could not rename the organization."),
    }),
  );

  return (
    <div className="space-y-6">
      <PageBreadcrumb
        crumbs={[
          { label: "Organizations", to: "/" },
          { label: organization.data?.name ?? "Organization", to: `/orgs/${orgId}` },
          { label: "Settings" },
        ]}
      />

      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Organization settings</h1>
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
                <Label htmlFor="slug">Slug</Label>
                <Input id="slug" value={organization.data?.slug ?? ""} readOnly disabled />
                <p className="text-xs text-muted-foreground">
                  {/* Renaming deliberately leaves this alone: it is the stable handle, and
                      silently repointing it would break anything referring to it. */}
                  Fixed once created, so links and scripts that use it keep working.
                </p>
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
