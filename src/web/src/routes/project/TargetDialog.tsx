
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Field, FieldContent, FieldDescription, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import {
  Select, SelectContent, SelectItem, SelectTrigger,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAddTarget, useCredentials, useUpdateTarget } from "@/api/queries";
import { SdkLanguage, languageLabel, registryLabel } from "@/api/types";
import type { SdkTarget } from "@/api/types";

/** The empty choice, named: a target that publishes nowhere. */
const NO_PUBLISH = "none";

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
  // "" means this target does not publish. Naming that rather than leaving it as an absence
  // is what lets the switch below be disabled honestly: a target with nowhere to publish
  // cannot publish automatically.
  const [credentialId, setCredentialId] = useState(target?.registryCredentialId ?? NO_PUBLISH);
  const [autoPublish, setAutoPublish] = useState(target?.autoPublish ?? false);
  const credentials = useCredentials(orgId);
  const usable = (credentials.data ?? []).filter((c) => !c.isRevoked);
  const publishes = credentialId !== NO_PUBLISH;
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
          registryCredentialId: credentialId === NO_PUBLISH ? null : credentialId,
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
        registryCredentialId: credentialId === NO_PUBLISH ? null : credentialId,
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

          <FieldGroup className="py-4">
            <Field>
              <FieldLabel htmlFor="package-name">Package name</FieldLabel>
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
                <FieldDescription>
                  {target.packageName}@{target.lastPublishedVersion} stays on{" "}
                  {registryLabel[target.registry]}. Future versions go to the new name, and this
                  target no longer reports that release.
                </FieldDescription>
              )}
            </Field>

            <Field>
              <FieldLabel htmlFor="credential">Publish with</FieldLabel>
              <Select
                value={credentialId}
                onValueChange={(value) => {
                  const chosen = (value as string | null) ?? NO_PUBLISH;
                  setCredentialId(chosen);
                  // Nowhere to publish means nothing to publish automatically. Left on, the
                  // switch would promise something every run would then fail to do.
                  if (chosen === NO_PUBLISH) setAutoPublish(false);
                }}
              >
                <SelectTrigger
                  id="credential"
                  // Two lines, so the fixed height goes and the padding matches an Input's
                  // rhythm rather than pressing the text against the border.
                  className="h-auto w-full items-center py-2 whitespace-normal"
                >
                  {/* Rendered here rather than by SelectValue, which falls back to the raw
                      value when an item's children are anything but a plain string. */}
                  {/* flex-1: the block always takes the room left by the chevron, so choosing a
                      shorter or longer option cannot resize it — and the truncation is
                      measured against a box that never moves. */}
                  <span className="flex min-w-0 flex-1 flex-col items-start gap-0.5 text-left">
                    <span className="w-full truncate leading-tight">
                      {publishes
                        ? (usable.find((c) => c.id === credentialId)?.label ?? "Unknown credential")
                        : "Do not publish"}
                    </span>
                    <span
                      className="w-full truncate font-mono text-xs leading-tight text-muted-foreground"
                      title={usable.find((c) => c.id === credentialId)?.registryUrl}
                    >
                      {publishes
                        ? usable.find((c) => c.id === credentialId)?.registryUrl
                        : "Generate and build only"}
                    </span>
                  </span>
                </SelectTrigger>

                <SelectContent>
                  <SelectItem value={NO_PUBLISH}>
                    <span className="flex min-w-0 flex-col items-start gap-0.5 py-0.5">
                      <span className="leading-tight">Do not publish</span>
                      <span className="text-xs leading-tight text-muted-foreground">
                        Generate and build only; the artifact stays here.
                      </span>
                    </span>
                  </SelectItem>

                  {usable.map((credential) => (
                    <SelectItem key={credential.id} value={credential.id}>
                      <span className="flex min-w-0 flex-col items-start gap-0.5 py-0.5">
                        <span className="w-full truncate leading-tight">{credential.label}</span>
                        <span
                          className="w-full truncate font-mono text-xs leading-tight text-muted-foreground"
                          title={credential.registryUrl}
                        >
                          {credential.registryUrl}
                        </span>
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {usable.length === 0 && (
                <FieldDescription>
                  This organization has no usable npm credential yet. Generating and building
                  work without one; only publishing needs it.
                </FieldDescription>
              )}
            </Field>

            {/* A switch, like every other setting in the app: this takes effect when it is
                saved, and a bare checkbox reads as one item of a list rather than a state
                the target is in. */}
            <Field orientation="horizontal">
              {/* Content first, switch last: the row reads label-then-state, the way it did
                  when a justify-between div held the two apart. */}
              <FieldContent>
                <FieldLabel htmlFor="auto-publish" className="text-sm font-medium">
                  Publish automatically
                </FieldLabel>
                <FieldDescription>
                  Every new spec version is generated, built and pushed to {registry} with no
                  human step. Off by default — publishing to a public registry cannot be undone.
                </FieldDescription>
              </FieldContent>

              <Switch
                id="auto-publish"
                checked={autoPublish}
                disabled={!publishes}
                onCheckedChange={(checked) => setAutoPublish(checked)}
              />
            </Field>
          </FieldGroup>

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
