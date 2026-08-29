import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAddTarget, useCredentials, useUpdateTarget } from "@/api/queries";
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
  // "" is the unset choice: with one usable credential the publish step finds it on its own,
  // which keeps the common case free of a decision nobody has the information to make yet.
  const [credentialId, setCredentialId] = useState(target?.registryCredentialId ?? "");
  const [autoPublish, setAutoPublish] = useState(target?.autoPublish ?? false);
  const credentials = useCredentials(orgId);
  const usable = (credentials.data ?? []).filter((c) => !c.isRevoked);
  const add = useAddTarget(orgId, projectId);
  const update = useUpdateTarget(orgId, projectId);

  // TypeScript is the only language wired end to end, so a new target can only be an npm one.
  const registry = target ? registryLabel[target.registry] : "npm";
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
        {
          targetId: target.id,
          packageName: packageName.trim(),
          autoPublish,
          registryCredentialId: credentialId || null,
        },
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
      {
        language: SdkLanguage.TypeScript,
        packageName,
        autoPublish,
        registryCredentialId: credentialId || null,
      },
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

            <div className="space-y-2">
              <Label htmlFor="credential">Publish with</Label>
              <select
                id="credential"
                value={credentialId}
                onChange={(e) => setCredentialId(e.target.value)}
                className="h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                {/* Naming the consequence rather than the absence: the endpoint lives on the
                    credential, so this choice is where the package ends up. */}
                <option value="">
                  {usable.length === 0
                    ? "No credential yet — publishing will fail"
                    : usable.length > 1
                      ? "Choose a credential — this organization has several"
                      : "The organization's npm credential"}
                </option>
                {usable.map((credential) => (
                  <option key={credential.id} value={credential.id}>
                    {credential.label} → {credential.registryUrl}
                  </option>
                ))}
              </select>
              <p className="text-xs text-muted-foreground">
                {usable.length === 0
                  ? "This organization has no usable npm credential. Generating and building work without one; only publishing needs it."
                  : usable.length > 1
                    ? "Several credentials can publish npm packages, and they may point at different registries. Leave this unset and a run refuses rather than guessing."
                    : "Where this SDK is published: the registry that credential points at."}
              </p>
            </div>

            {/* A switch, like every other setting in the app: this takes effect when it is
                saved, and a bare checkbox reads as one item of a list rather than a state
                the target is in. */}
            <div className="flex items-start justify-between gap-4">
              <div className="space-y-1">
                <Label htmlFor="auto-publish" className="text-sm font-medium">
                  Publish automatically
                </Label>
                <p className="text-xs text-muted-foreground">
                  Every new spec version is generated, built and pushed to {registry} with no
                  human step. Off by default — publishing to a public registry cannot be undone.
                </p>
              </div>

              <Switch
                id="auto-publish"
                checked={autoPublish}
                onCheckedChange={(checked) => setAutoPublish(checked)}
              />
            </div>

            {/* Said before it is switched on rather than discovered four runs later: every
                spec upload would queue a run that builds, then fails on the last step. */}
            {autoPublish && usable.length === 0 && (
              <p className="text-sm text-[var(--code-warning)]">
                Nothing can be published until this organization has an npm credential. Every
                new spec version will build and then fail at the publish step.
              </p>
            )}
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
