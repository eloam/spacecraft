import { useParams } from "react-router-dom";
import { ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { usePublications } from "@/api/queries";
import { registryLabel } from "@/api/types";

/** Mirrors SpaceCraft.Domain.Registries.PublicationStatus. */
const PublicationStatus = {
  Pending: 1,
  Succeeded: 2,
  AlreadyExists: 3,
  Failed: 4,
} as const;

function StatusBadge({ status }: { status: number }) {
  if (status === PublicationStatus.Succeeded) {
    return <Badge variant="secondary">Published</Badge>;
  }
  if (status === PublicationStatus.AlreadyExists) {
    // Not a failure: registries are append-only, so re-running a pipeline is a no-op.
    return <Badge variant="outline">Already there</Badge>;
  }
  if (status === PublicationStatus.Failed) {
    return <Badge variant="destructive">Failed</Badge>;
  }
  return <Badge variant="outline">Pending</Badge>;
}

export function PublicationsPage() {
  const { orgId = "" } = useParams();
  const publications = usePublications(orgId);

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm text-muted-foreground">
          Every version this organization has pushed to a registry.
        </p>
      </div>

      <QueryBoundary isLoading={publications.isLoading} error={publications.error} rows={4}>
        {publications.data && publications.data.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Package</TableHead>
                <TableHead>Version</TableHead>
                <TableHead>Registry</TableHead>
                <TableHead>Project</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Published</TableHead>
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
                    <StatusBadge status={publication.status} />
                    {publication.failureReason && (
                      <p className="mt-1 text-xs text-destructive">{publication.failureReason}</p>
                    )}
                  </TableCell>
                  <TableCell className="text-right text-xs text-muted-foreground">
                    {new Date(publication.completedAt ?? publication.createdAt).toLocaleString()}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <EmptyState
            title="Nothing published yet"
            description="Generate an SDK with publishing enabled and it will appear here."
          />
        )}
      </QueryBoundary>
    </div>
  );
}
