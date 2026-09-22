import { useParams } from "react-router-dom";
import { useOrganizationId } from "@/hooks/useRouteIdentity";
import { ExternalLink, PackageCheck } from "lucide-react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { EmptyState } from "@/components/EmptyState";
import { PublicationStatusBadge } from "@/components/StatusBadge";
import { QueryBoundary } from "@/components/QueryBoundary";
import { TableCard } from "@/components/TableCard";
import { useOrganizations, usePublications } from "@/api/queries";
import { registryLabel } from "@/api/types";
import { formatDateTime } from "@/lib/format";

export function PublicationsPage() {
  const { orgSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const organization = useOrganizations().data?.find((o) => o.id === orgId);
  const publications = usePublications(orgId);

  return (
    <div className="space-y-6">
      <title>
        {organization ? `Published — ${organization.name} — SpaceCraft` : `Published — SpaceCraft`}
      </title>

      <div>
        <h1 className="font-heading text-2xl font-medium tracking-tight">Published</h1>
        <p className="text-sm text-muted-foreground">
          Every version this organization has pushed to a registry.
        </p>
      </div>

      <QueryBoundary
        isLoading={publications.isLoading}
        error={publications.error}
        hasData={publications.data !== undefined}
        rows={4}
      >
        {publications.data && publications.data.length > 0 ? (
          <TableCard>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Package</TableHead>
                  <TableHead>Version</TableHead>
                  <TableHead>Registry</TableHead>
                  <TableHead>Project</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">Completed</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {publications.data.map((publication) => (
                  <TableRow key={publication.id}>
                    <TableCell className="font-mono text-xs">
                      {publication.registryUrl ? (
                        <a
                          href={publication.registryUrl}
                          target="_blank"
                          rel="noreferrer noopener"
                          className="inline-flex items-center gap-1 hover:underline"
                        >
                          {publication.packageName}
                          <ExternalLink className="size-3" aria-hidden />
                          <span className="sr-only"> (opens in a new tab)</span>
                        </a>
                      ) : (
                        publication.packageName
                      )}
                    </TableCell>
                    <TableCell className="font-mono text-xs">{publication.version}</TableCell>
                    <TableCell className="text-xs">{registryLabel[publication.registry]}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {publication.projectName ?? "—"}
                    </TableCell>
                    <TableCell>
                      <PublicationStatusBadge status={publication.status} />
                      {publication.failureReason && (
                        // Capped and wrapped against the cell's inherited nowrap: a registry
                        // rejection is a whole npm error, and on one line it made the Status
                        // column as wide as the message — pushing Completed off-screen for
                        // every other row. wrap-anywhere, because the registry URL inside it
                        // has no spaces to break at.
                        <p className="mt-1 max-w-xs text-xs wrap-anywhere whitespace-normal text-destructive">
                          {publication.failureReason}
                        </p>
                      )}
                    </TableCell>
                    <TableCell className="text-right text-xs text-muted-foreground">
                      {/* Null until the registry has answered: a pending publication has no
                          completion time, and showing createdAt in its place said "published"
                          about something that might still fail. */}
                      {publication.completedAt ? (
                        <time dateTime={publication.completedAt}>
                          {formatDateTime(publication.completedAt)}
                        </time>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableCard>
        ) : (
          <EmptyState
            icon={<PackageCheck aria-hidden />}
            title="Nothing published yet"
            // Named after the control that does it: "publishing enabled" sent people
            // looking for a toggle by that name, and the field is called "Publish with".
            description="Give an SDK target a credential under Publish with, then generate — releases appear here."
          />
        )}
      </QueryBoundary>
    </div>
  );
}
