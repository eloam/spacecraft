import { useState } from "react";
import { Link } from "react-router-dom";
import { Building2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Item, ItemContent, ItemDescription } from "@/components/ui/item";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { CreateOrganizationDialog } from "@/components/CreateOrganizationDialog";
import { useOrganizations } from "@/api/queries";
import { roleLabel } from "@/api/types";

export function OrganizationsPage() {
  const { data, isLoading, error } = useOrganizations();
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
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

      <QueryBoundary isLoading={isLoading} error={error}>
        {data?.length ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((org) => (
              // The whole tile is the link. It used to be a card with an absolutely
              // positioned link stretched over it and a settings cog floating above that,
              // stacking three things where one was meant: Item renders as the anchor
              // itself, so the hover state and the focus ring come with it.
              <Item
                key={org.id}
                variant="outline"
                className="h-full flex-col items-stretch bg-card"
                render={<Link to={`/orgs/${org.slug}`} />}
              >
                <ItemContent className="gap-1">
                  <ItemDescription className="text-xs font-medium tracking-wider uppercase">
                    {roleLabel[org.role] ?? "Member"}
                  </ItemDescription>
                  <span className="truncate font-heading text-lg font-semibold">{org.name}</span>
                  <span className="truncate font-mono text-xs text-muted-foreground">
                    {org.slug}
                  </span>
                </ItemContent>

                <ItemContent className="flex-none">
                  <span className="text-sm text-muted-foreground">
                    {org.projectCount} {org.projectCount === 1 ? "project" : "projects"}
                  </span>
                </ItemContent>
              </Item>
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
