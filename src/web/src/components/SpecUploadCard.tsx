import { useRef, useState } from "react";
import { CopyCheck, FileUp, SquareCode, TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { useUploadSpec } from "@/api/queries";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

/** Drop zone plus file picker for a new OpenAPI document. */
export function SpecUploadCard({
  orgId,
  projectId,
  onViewFindings,
}: {
  orgId: string;
  projectId: string;
  /** Opens the validation dialog on a version this card just created. */
  onViewFindings?: (specId: string) => void;
}) {
  const upload = useUploadSpec(orgId, projectId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [findingCount, setFindingCount] = useState(0);
  const [duplicate, setDuplicate] = useState<{ fileName: string; detail: string } | null>(null);
  const [uploaded, setUploaded] = useState<string | null>(null);

  function send(file: File) {
    setFindingCount(0);
    setUploaded(null);
    upload.mutate(file, {
      onSuccess: (result) => {
        setUploaded(result.specVersion.id);
        setFindingCount(result.diagnostics?.length ?? 0);
        const triggered = result.triggeredRunIds?.length ?? 0;
        toast.success(
          `Uploaded spec #${result.specVersion.sequence}` +
            (triggered ? ` — queued ${triggered} run${triggered === 1 ? "" : "s"}` : ""),
        );
      },
      onError: (err) => {
        // A refused duplicate is not a failure to glance at and lose. It names a version and
        // a date, says nothing changed, and is the one error someone will want to read twice
        // — a toast would truncate it and take it away.
        if (err instanceof ApiError && err.status === 409) {
          setDuplicate({ fileName: file.name, detail: err.detail });
          return;
        }

        toast.error(err instanceof ApiError ? err.detail : "The upload failed.");
      },
    });
  }

  return (
    <>
      <div className="space-y-4">
        {/* The same Empty composition every other panel in the app uses — icon in its
            rounded surface, serif title, muted line under it. The drop zone says what to
            do rather than what is there, but there is no reason for it to say so in a
            different visual language. */}
        <Empty
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => {
            e.preventDefault();
            setDragging(false);
            const file = e.dataTransfer.files[0];
            if (file) send(file);
          }}
          className={cn(
            "rounded-2xl border border-dashed transition-colors",
            dragging && "border-foreground/40 bg-muted/50",
          )}
        >
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <FileUp aria-hidden />
            </EmptyMedia>
            <EmptyTitle>Drop an OpenAPI document here</EmptyTitle>
            <EmptyDescription>
              {/* Non-breaking: EmptyHeader is max-w-sm, and the line broke between the
                  number and its unit. */}
              .yaml, .yml or .json — up to 10&nbsp;MB. External <code>$ref</code>s are
              rejected.
            </EmptyDescription>
          </EmptyHeader>

          <EmptyContent>
            <input
              ref={inputRef}
              type="file"
              accept=".yaml,.yml,.json,application/json,application/yaml"
              className="sr-only"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) send(file);
                e.target.value = "";
              }}
            />

            <Button
              type="button"
              variant="outline"
              onClick={() => inputRef.current?.click()}
              disabled={upload.isPending}
            >
              {upload.isPending ? "Uploading…" : "Choose a file"}
            </Button>
          </EmptyContent>
        </Empty>

        {findingCount > 0 && (
          <Alert>
            <TriangleAlert className="size-4" aria-hidden />
            <AlertTitle>
              {findingCount} {findingCount === 1 ? "finding" : "findings"}
            </AlertTitle>
            <AlertDescription className="space-y-3">
              <p>
                {/* This used to list the first eight and say "…and 718 more", with nothing
                    leading anywhere. The viewer shows every one against the line it came
                    from, and this is the moment someone wants to read them. */}
                Every finding is shown against the document it came from.
              </p>
              {uploaded && onViewFindings && (
                <Button size="sm" variant="outline" onClick={() => onViewFindings(uploaded)}>
                  <SquareCode className="size-4" aria-hidden />
                  Review findings
                </Button>
              )}
            </AlertDescription>
          </Alert>
        )}
      </div>

      <Dialog open={duplicate !== null} onOpenChange={(open) => !open && setDuplicate(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <CopyCheck className="size-4 text-muted-foreground" aria-hidden />
              Already uploaded
            </DialogTitle>
            <DialogDescription>{duplicate?.detail}</DialogDescription>
          </DialogHeader>

          <p className="text-sm text-muted-foreground">
            <span className="font-mono">{duplicate?.fileName}</span> was not added. Change the
            document, or delete the version it matches if you want to run the pipeline over it
            again.
          </p>

          <DialogFooter showCloseButton />
        </DialogContent>
      </Dialog>
    </>
  );
}
