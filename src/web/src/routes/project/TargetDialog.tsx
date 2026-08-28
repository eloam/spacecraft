import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAddTarget, useUpdateTarget } from "@/api/queries";
import { SdkLanguage, languageLabel, registryLabel } from "@/api/types";
import type { SdkTarget } from "@/api/types";

/**
 * Adds a target, or edits the one it is given.
 *
 * One dialog for both because the two forms are the same form: language and registry are
 * decided at creation and never change, so what can be edited is exactly what was asked for.
 */
export function TargetDialog({
  orgId,
  projectId,
  target,
  open,
  onOpenChange,
}: {
  orgId: string;
  projectId: string;
  /** The target being edited, or undefined to add one. */
  target?: SdkTarget;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  // Seeded from the row this was opened on. The caller remounts on every opening, so these
  // initial values are read again each time rather than surviving from the last one.
  const [packageName, setPackageName] = useState(target?.packageName ?? "");
  const [autoPublish, setAutoPublish] = useState(target?.autoPublish ?? false);
  const add = useAddTarget(orgId, projectId);
  const update = useUpdateTarget(orgId, projectId);

  const pending = add.isPending || update.isPending;
  const renaming = Boolean(target) && packageName.trim() !== target?.packageName;

  function submit(event: React.FormEvent) {
    event.preventDefault();

    const onError = (err: unknown) =>
      toast.error(
        err instanceof ApiError
          ? err.detail
          : `Could not ${target ? "save the target" : "add the target"}.`,
      );

    if (target) {
      update.mutate(
        { targetId: target.id, packageName: packageName.trim(), autoPublish },
        {
          onSuccess: () => {
            toast.success(`Saved ${packageName.trim()}`);
            onOpenChange(false);
          },
          onError,
        },
      );
      return;
    }

    add.mutate(
      // TypeScript is the only language wired end to end today; the API rejects the rest.
      { language: SdkLanguage.TypeScript, packageName, autoPublish },
      {
        onSuccess: () => {
          toast.success(`Added ${packageName}`);
          setPackageName("");
          onOpenChange(false);
        },
        onError,
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>{target ? "Edit the SDK target" : "Add an SDK target"}</DialogTitle>
            <DialogDescription>
              {target
                ? `${languageLabel[target.language]} publishes to ${registryLabel[target.registry]}. Neither can be changed — add another target to publish elsewhere.`
                : "TypeScript publishes to npm. Other languages are modelled but not yet wired up."}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="package-name">Package name</Label>
              <Input
                id="package-name"
                value={packageName}
                onChange={(e) => setPackageName(e.target.value)}
                placeholder="@acme/billing-sdk"
                autoFocus
              />
              {/* Said before the click, not after: what is already on the registry stays
                  there under its own name, and this target stops updating it. */}
              {renaming && target?.lastPublishedVersion && (
                <p className="text-xs text-muted-foreground">
                  {target.packageName}@{target.lastPublishedVersion} stays on{" "}
                  {registryLabel[target.registry]}. Future versions go to the new name, and this
                  target no longer reports that release.
                </p>
              )}
            </div>

            <label className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                checked={autoPublish}
                onChange={(e) => setAutoPublish(e.target.checked)}
                className="mt-0.5"
              />
              <span>
                Publish automatically on every new spec version
                <span className="block text-xs text-muted-foreground">
                  Off by default — publishing to a public registry cannot be undone.
                </span>
              </span>
            </label>
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!packageName.trim() || pending}>
              {pending
                ? target ? "Saving…" : "Adding…"
                : target ? "Save changes" : "Add target"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
