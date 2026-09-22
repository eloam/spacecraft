import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useOrganizationId } from "@/hooks/useRouteIdentity";
import { Building2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { RenameResourceForm } from "@/components/RenameResourceForm";
import { DangerSection, SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { plural } from "@/lib/format";
import {
  useDeleteOrganization, useOrganization, useProjects, useRenameOrganization,
} from "@/api/queries";

export function OrganizationSettingsPage() {
  const { orgSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const navigate = useNavigate();
  const organization = useOrganization(orgId);
  const projects = useProjects(orgId);
  const rename = useRenameOrganization(orgId);
  const remove = useDeleteOrganization();

  const [deleteOpen, setDeleteOpen] = useState(false);

  return (
    <div className="space-y-6">
      <title>
        {organization.data
          ? `Settings — ${organization.data.name} — SpaceCraft`
          : `Settings — SpaceCraft`}
      </title>

      <div>
        <h1 className="font-heading text-2xl font-medium tracking-tight">Settings</h1>
        <p className="text-sm text-muted-foreground">
          Naming and removal for this organization.
        </p>
      </div>

      {/* None of this may render without the organization: the Name field would sit empty,
          the slug blank, and the delete dialog's typed-name gate would be undefined —
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
        {organization.data && (
          <>
            <SettingsSection
              icon={<Building2 />}
              title="General"
              description="How this organization is named."
            >
              <RenameResourceForm
                resourceKind="organization"
                name={organization.data.name}
                slug={organization.data.slug}
                isPending={rename.isPending}
                onSubmit={async ({ name }) => {
                  const updated = await rename.mutateAsync(name);
                  toast.success("Organization renamed.");
                  // The slug moved with the name, so the address bar is now pointing at a
                  // slug that no longer resolves. Replace rather than push: the old URL is
                  // not somewhere to go back to.
                  if (updated.slug !== orgSlug) {
                    navigate(`/orgs/${updated.slug}/settings`, { replace: true });
                  }
                }}
              />
            </SettingsSection>

            <DangerSection
              icon={<Trash2 />}
              title="Delete organization"
              description="Removes every project, run, credential and publication record it owns."
            >
              {/* self-start: CardContent is a flex column, which stretches its children,
                  and a delete button as wide as the card reads as the section itself. */}
              <Button
                variant="destructive"
                className="self-start"
                onClick={() => setDeleteOpen(true)}
              >
                <Trash2 className="size-4" aria-hidden />
                Delete organization
              </Button>
            </DangerSection>

            <ConfirmDeleteDialog
              open={deleteOpen}
              onOpenChange={setDeleteOpen}
              onOpenChangeComplete={(open) => {
                if (!open) remove.reset();
              }}
              title={`Delete ${organization.data.name}?`}
              description="Everything this organization owns goes with it. This cannot be undone."
              confirmText={organization.data.name}
              consequences={[
                // The project list is its own query, and it can fail while the organization
                // loads. "0 projects" would then understate the blast radius of the one
                // delete that takes everything, so count only when there is a count.
                projects.data
                  ? `${plural(projects.data.length, "project")}, with their specs and run history`
                  : "Every project in it, with their specs and run history",
                "Registry credentials stored for this organization",
                "The record of what it has published",
              ]}
              reassurances={["Packages already published stay in their registries"]}
              isPending={remove.isPending}
              error={remove.error}
              onConfirm={() =>
                remove.mutate(
                  { orgId, name: organization.data.name },
                  {
                    onSuccess: () => {
                      toast.success("Organization deleted.");
                      navigate("/");
                    },
                  },
                )
              }
            />
          </>
        )}
      </QueryBoundary>
    </div>
  );
}
