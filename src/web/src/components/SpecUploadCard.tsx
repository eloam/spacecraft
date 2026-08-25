import { useRef, useState } from "react";
import { FileUp, TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { useUploadSpec } from "@/api/queries";
import type { SpecDiagnostic } from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

/** Drop zone plus file picker for a new OpenAPI document. */
export function SpecUploadCard({ orgId, projectId }: { orgId: string; projectId: string }) {
  const upload = useUploadSpec(orgId, projectId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [diagnostics, setDiagnostics] = useState<SpecDiagnostic[]>([]);

  function send(file: File) {
    setDiagnostics([]);
    upload.mutate(file, {
      onSuccess: (result) => {
        setDiagnostics(result.diagnostics ?? []);
        const triggered = result.triggeredRunIds?.length ?? 0;
        toast.success(
          `Uploaded spec #${result.specVersion.sequence}` +
            (triggered ? ` — queued ${triggered} run${triggered === 1 ? "" : "s"}` : ""),
        );
      },
      onError: (err) =>
        toast.error(err instanceof ApiError ? err.detail : "The upload failed."),
    });
  }

  return (
    <div className="space-y-4">
      <div
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
        className={`flex flex-col items-center justify-center rounded-xl border border-dashed px-6 py-10 text-center transition-colors ${
          dragging ? "border-foreground/40 bg-muted/50" : ""
        }`}
      >
        <FileUp className="mb-3 size-7 text-muted-foreground" aria-hidden />
        <p className="text-sm font-medium">Drop an OpenAPI document here</p>
        <p className="mt-1 text-sm text-muted-foreground">
          .yaml, .yml or .json — up to 10 MB. External <code>$ref</code>s are rejected.
        </p>

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
          className="mt-5"
          onClick={() => inputRef.current?.click()}
          disabled={upload.isPending}
        >
          {upload.isPending ? "Uploading…" : "Choose a file"}
        </Button>
      </div>

      {diagnostics.length > 0 && (
        <Alert>
          <TriangleAlert className="size-4" aria-hidden />
          <AlertTitle>
            {diagnostics.length} {diagnostics.length === 1 ? "diagnostic" : "diagnostics"}
          </AlertTitle>
          <AlertDescription>
            <ul className="mt-2 space-y-1 text-xs">
              {diagnostics.slice(0, 8).map((d, i) => (
                <li key={i} className="font-mono">
                  <span className="text-muted-foreground">{d.jsonPointer ?? "-"}</span> {d.message}
                </li>
              ))}
              {diagnostics.length > 8 && (
                <li className="text-muted-foreground">…and {diagnostics.length - 8} more</li>
              )}
            </ul>
          </AlertDescription>
        </Alert>
      )}
    </div>
  );
}
