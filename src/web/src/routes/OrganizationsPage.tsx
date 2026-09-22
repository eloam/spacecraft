import { useState } from "react";
import { Building2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ItemContent, ItemDescription } from "@/components/ui/item";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { CreateOrganizationDialog } from "@/components/CreateOrganizationDialog";
import { TileLink } from "@/components/TileLink";
import { useOrganizations } from "@/api/queries";
import { roleLabel } from "@/api/types";
import { plural } from "@/lib/format";

export function OrganizationsPage() {
  const { data, isLoading, error } = useOrganizations();
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      {/* React 19 hoists a <title> rendered anywhere into <head>, so every page names itself
          this way — page first, product last — and layouts and dialogs never do, so only one
          is rendered at a time. A single string, since a title takes no other children. */}
      <title>Organizations — SpaceCraft</title>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-medium tracking-tight">Organizations</h1>
          <p className="text-sm text-muted-foreground">
            Each organization owns its own projects, registry credentials and pipeline history.
          </p>
        </div>
        <Button onClick={() => setOpen(true)}>
          <Plus className="size-4" aria-hidden />
          New organization
        </Button>
      </div>

      <QueryBoundary isLoading={isLoading} error={error} hasData={data !== undefined}>
        {data?.length ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((org) => (
              <TileLink key={org.id} to={`/orgs/${org.slug}`}>
                <ItemContent className="gap-1">
                  <ItemDescription className="text-xs font-medium tracking-wider uppercase">
                    {roleLabel[org.role] ?? "Unknown"}
                  </ItemDescription>
                  <span className="truncate font-heading text-lg font-semibold">{org.name}</span>
                  <span className="truncate font-mono text-xs text-muted-foreground">
                    {org.slug}
                  </span>
                </ItemContent>

                <ItemContent className="flex-none">
                  <span className="text-sm text-muted-foreground">
                    {plural(org.projectCount, "project")}
                  </span>
                </ItemContent>
              </TileLink>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={<Building2 aria-hidden />}
            title="No organizations yet"
            description="Create one to start tracking an API and publishing SDKs from it."
            action={
              <Button onClick={() => setOpen(true)}>
                <Plus className="size-4" aria-hidden />
                New organization
              </Button>
            }
          />
        )}
      </QueryBoundary>

      <CreateOrganizationDialog open={open} onOpenChange={setOpen} />
    </div>
  );
}
