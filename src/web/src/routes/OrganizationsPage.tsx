import { useState } from "react";
import { Link } from "react-router-dom";
import { Building2, Plus, Settings } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
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
          <h1 className="text-2xl font-semibold tracking-tight">Organizations</h1>
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
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((org) => (
              // The card is the link; settings sit inside it as their own control, so the
              // organization's own page is reachable without entering it first — which was
              // the only way to find them before.
              <Card
                key={org.id}
                className="relative h-full transition-colors hover:border-foreground/20"
              >
                <Link to={`/orgs/${org.slug}`} className="absolute inset-0" aria-label={org.name} />
                <CardHeader>
                  <div className="flex items-start justify-between gap-2">
                    <CardTitle className="truncate">{org.name}</CardTitle>
                    <Badge variant="secondary">{roleLabel[org.role] ?? "Member"}</Badge>
                  </div>
                  <CardDescription className="font-mono text-xs">{org.slug}</CardDescription>
                </CardHeader>
                <CardContent className="flex items-end justify-between text-sm text-muted-foreground">
                  <span>
                    {org.projectCount} {org.projectCount === 1 ? "project" : "projects"}
                  </span>
                  <Link
                    to={`/orgs/${org.slug}/settings`}
                    // Above the card-wide link, so it wins the click.
                    className={`relative z-10 ${buttonVariants({ variant: "ghost", size: "sm" })}`}
                  >
                    <Settings className="size-4" aria-hidden />
                    <span className="sr-only">{org.name} settings</span>
                  </Link>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={<Building2 className="size-8" />}
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
