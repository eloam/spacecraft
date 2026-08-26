import { useState } from "react";
import { useParams } from "react-router-dom";
import { QueryBoundary } from "@/components/QueryBoundary";
import { SpecUploadCard } from "@/components/SpecUploadCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SpecValidationBadge } from "@/routes/project/SpecValidationBadge";
import { DiagnosticsDialog } from "@/routes/project/DiagnosticsDialog";
import { useSpecs } from "@/api/queries";

/** Uploaded OpenAPI documents and what validation made of them. */
export function ProjectSpecsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const specs = useSpecs(orgId, projectId);
  const [diagnosticsFor, setDiagnosticsFor] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <SpecUploadCard orgId={orgId} projectId={projectId} />

      <QueryBoundary isLoading={specs.isLoading} error={specs.error}>
        {specs.data?.length ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>#</TableHead>
                <TableHead>API</TableHead>
                <TableHead>Version</TableHead>
                <TableHead>Validation</TableHead>
                <TableHead className="text-right">Uploaded</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {specs.data.map((spec) => (
                <TableRow key={spec.id}>
                  <TableCell className="font-mono text-xs">{spec.sequence}</TableCell>
                  <TableCell>{spec.apiTitle ?? "—"}</TableCell>
                  <TableCell className="font-mono text-xs">{spec.apiVersion ?? "—"}</TableCell>
                  <TableCell>
                    {spec.warningCount + spec.errorCount > 0 ? (
                      // The count was a dead end: it told you something was wrong
                      // without any way to find out what.
                      <button
                        type="button"
                        onClick={() => setDiagnosticsFor(spec.id)}
                        className="cursor-pointer rounded-md focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
                        aria-label={`Show ${spec.errorCount + spec.warningCount} validation findings`}
                      >
                        <SpecValidationBadge
                          status={spec.validationStatus}
                          warnings={spec.warningCount}
                          errors={spec.errorCount}
                        />
                      </button>
                    ) : (
                      <SpecValidationBadge
                        status={spec.validationStatus}
                        warnings={spec.warningCount}
                        errors={spec.errorCount}
                      />
                    )}
                  </TableCell>
                  <TableCell className="text-right text-xs text-muted-foreground">
                    {new Date(spec.createdAt).toLocaleString()}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </QueryBoundary>

      <DiagnosticsDialog
        orgId={orgId}
        projectId={projectId}
        specId={diagnosticsFor}
        onClose={() => setDiagnosticsFor(null)}
      />
    </div>
  );
}
