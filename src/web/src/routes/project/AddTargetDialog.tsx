import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAddTarget } from "@/api/queries";
import { SdkLanguage } from "@/api/types";

export function AddTargetDialog({
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
  const [packageName, setPackageName] = useState("");
  const [autoPublish, setAutoPublish] = useState(false);
  const add = useAddTarget(orgId, projectId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    add.mutate(
      // TypeScript is the only language wired end to end today; the API rejects the rest.
      { language: SdkLanguage.TypeScript, packageName, autoPublish },
      {
        onSuccess: () => {
          toast.success(`Added ${packageName}`);
          setPackageName("");
          onOpenChange(false);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not add the target."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Add an SDK target</DialogTitle>
            <DialogDescription>
              TypeScript publishes to npm. Other languages are modelled but not yet wired up.
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
            <Button type="submit" disabled={!packageName.trim() || add.isPending}>
              {add.isPending ? "Adding…" : "Add target"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
