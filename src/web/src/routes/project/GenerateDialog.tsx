import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useCreateRun, useProject, useSpecs } from "@/api/queries";
import { SpecValidationStatus, languageLabel, stepKindLabel } from "@/api/types";

export function GenerateDialog({
  orgId,
  projectId,
  open,
  onOpenChange,
}: {
  orgId: string;
  projectId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const navigate = useNavigate();
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);
  const createRun = useCreateRun(orgId);

  const usableSpecs = specs.data?.items.filter((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? [];
  const targets = project.data?.targets ?? [];

  const [specId, setSpecId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [publish, setPublish] = useState(false);

  const selectedSpec = specId || usableSpecs[0]?.id || "";
  const selectedTarget = targetId || targets[0]?.id || "";

  function submit(event: React.FormEvent) {
    event.preventDefault();
    createRun.mutate(
      { specVersionId: selectedSpec, sdkTargetId: selectedTarget, publish },
      {
        onSuccess: (run) => {
          onOpenChange(false);
          // Queuing a run is the point of this dialog; leaving the user on the tab they
          // started from means the thing they asked for happens somewhere they cannot see.
          navigate(`/orgs/${orgId}/runs/${run.id}`);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not queue the run."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Generate an SDK</DialogTitle>
            <DialogDescription>
              Runs {Object.values(stepKindLabel).join(" → ").toLowerCase()}.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="spec">Spec version</Label>
              <select
                id="spec"
                value={selectedSpec}
                onChange={(e) => setSpecId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {usableSpecs.map((spec) => (
                  <option key={spec.id} value={spec.id}>
                    #{spec.sequence} — {spec.apiTitle ?? "untitled"} {spec.apiVersion ?? ""}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-2">
              <Label htmlFor="target">Target</Label>
              <select
                id="target"
                value={selectedTarget}
                onChange={(e) => setTargetId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {targets.map((target) => (
                  <option key={target.id} value={target.id}>
                    {languageLabel[target.language]} — {target.packageName}
                  </option>
                ))}
              </select>
            </div>

            <label htmlFor="publish" className="flex items-start gap-2 text-sm">
              <input
                id="publish"
                type="checkbox"
                checked={publish}
                onChange={(e) => setPublish(e.target.checked)}
                className="mt-0.5"
              />
              <span>
                Publish to the registry after a successful build
                <span className="block text-xs text-muted-foreground">
                  Requires a registry credential and cannot be undone once accepted.
                </span>
              </span>
            </label>
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!selectedSpec || !selectedTarget || createRun.isPending}>
              {createRun.isPending ? "Queueing…" : "Queue run"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The findings behind a spec's warning count.
 *
 * A large document produces a lot of them — the Keycloak Admin API yields 413 — so the list
 * is virtualised by nothing more than a scroll container, and only fetched once opened.
 */
