import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { TriangleAlert, XCircle } from "lucide-react";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useSpecDiagnostics } from "@/api/queries";

export function DiagnosticsDialog({
  orgId,
  projectId,
  specId,
  onClose,
}: {
  orgId: string;
  projectId: string;
  specId: string | null;
  onClose: () => void;
}) {
  const diagnostics = useSpecDiagnostics(orgId, projectId, specId);

  return (
    <Dialog open={specId !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Validation findings</DialogTitle>
          <DialogDescription>
            {diagnostics.data
              ? `${diagnostics.data.length} finding${diagnostics.data.length === 1 ? "" : "s"} recorded when this spec was uploaded.`
              : "Loading…"}
          </DialogDescription>
        </DialogHeader>

        <div className="max-h-96 overflow-y-auto rounded-lg border">
          {diagnostics.isPending ? (
            <div className="space-y-2 p-3">
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-4 w-2/3" />
            </div>
          ) : (
            <ul className="divide-y text-sm">
              {diagnostics.data?.map((finding, index) => (
                <li key={index} className="flex gap-2 p-3">
                  {finding.severity === 2 ? (
                    <XCircle className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
                  ) : (
                    <TriangleAlert className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
                  )}
                  <div className="min-w-0">
                    <p>{finding.message}</p>
                    {finding.pointer && (
                      <p className="mt-0.5 font-mono text-xs break-all text-muted-foreground">
                        {finding.pointer}
                      </p>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
