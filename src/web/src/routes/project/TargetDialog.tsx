import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
import { Select, SelectContent, SelectItem, SelectTrigger } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError, describeError } from "@/api/client";
import { useAddTarget, useCredentials, useUpdateTarget } from "@/api/queries";
import { SdkLanguage, type SdkTarget, languageLabel, registryLabel } from "@/api/types";

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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <TargetForm
          orgId={orgId}
          projectId={projectId}
          target={target}
          onDone={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  );
}

/**
 * The form is a child of the content, which Base UI unmounts once the dialog has closed, so
 * its fields are seeded from `target` at every opening and go with the rest of the state on
 * every close — Cancel and Escape included. Held by the dialog component instead, the seed was
 * read once, when the page mounted, and every caller owed the dialog a remount per opening for
 * the row it was opened on to be read at all.
 */
function TargetForm({
  orgId,
  projectId,
  target,
  onDone,
}: {
  orgId: string;
  projectId: string;
  target?: SdkTarget;
  /** Closes the dialog after a save. */
  onDone: () => void;
}) {
  const [packageName, setPackageName] = useState(target?.packageName ?? "");
  // Beside the field that caused it, not in a toast: a rejected package name is something to
  // look at while fixing it, and a notification is gone by the time the value is read back.
  const [packageNameError, setPackageNameError] = useState<string | null>(null);
  // NO_PUBLISH means this target does not publish. Naming that rather than leaving it as an absence
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

  function submit(event: FormEvent) {
    event.preventDefault();
    setPackageNameError(null);

    const onError = (err: unknown) => {
      const fieldMessage = err instanceof ApiError ? err.fieldError("packageName") : undefined;
      if (fieldMessage) {
        setPackageNameError(fieldMessage);
        return;
      }
      // A failure the API did not attribute to the field — a 409, a 500 — still has to be
      // said somewhere, or the dialog just sits there having apparently done nothing.
      toast.error(
        describeError(err, `Could not ${target ? "save the target" : "add the target"}.`),
      );
    };

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
            onDone();
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
          onDone();
        },
        onError,
      },
    );
  }

  return (
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
        <Field data-invalid={Boolean(packageNameError)}>
          <FieldLabel htmlFor="package-name">Package name</FieldLabel>
          <Input
            id="package-name"
            value={packageName}
            onChange={(e) => {
              // Cleared as it is retyped: leaving the field red while somebody corrects it
              // says the new value is wrong too, which nothing has checked yet.
              setPackageName(e.target.value);
              setPackageNameError(null);
            }}
            placeholder="@acme/billing-sdk"
            aria-invalid={Boolean(packageNameError)}
            autoFocus
          />
          {/* Said before the click, not after: what is already on the registry stays
              there under its own name, and this target stops updating it. */}
          {renaming && target?.lastPublishedVersion && (
            <FieldDescription>
              {target.packageName}@{target.lastPublishedVersion} stays on{" "}
              {registryLabel[target.registry]}. Future versions go to the new name, and this target
              no longer reports that release.
            </FieldDescription>
          )}
          <FieldError>{packageNameError}</FieldError>
        </Field>

        <Field>
          <FieldLabel htmlFor="credential">Publish with</FieldLabel>
          <Select
            value={credentialId}
            onValueChange={(value) => {
              // Base UI nulls the value only when the chosen item leaves an open list —
              // a credential revoked while the picker is up. A credential that has gone
              // has nowhere to publish to, so that reads as "Do not publish".
              const chosen = value ?? NO_PUBLISH;
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
                <span className="w-full truncate font-mono text-xs leading-tight text-muted-foreground">
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
                    <span className="w-full truncate font-mono text-xs leading-tight text-muted-foreground">
                      {credential.registryUrl}
                    </span>
                  </span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* "No credential yet" is a claim about the organization, so it waits for the
              list. Said of one still loading, or refused — listing credentials takes an
              admin, and a member sees a 403 — it claimed an absence nobody had checked. */}
          {credentials.isPending ? (
            <Skeleton className="h-4 w-3/4" />
          ) : credentials.isError ? (
            <FieldDescription>
              {describeError(credentials.error, "The credentials could not be loaded.")}
            </FieldDescription>
          ) : usable.length === 0 ? (
            <FieldDescription>
              This organization has no usable npm credential yet. Generating and building work
              without one; only publishing needs it.
            </FieldDescription>
          ) : null}
        </Field>

        {/* A switch, like every other setting in the app: this takes effect when it is
            saved, and a bare checkbox reads as one item of a list rather than a state
            the target is in. */}
        <Field orientation="horizontal">
          {/* Content first, switch last: the row reads label-then-state. */}
          <FieldContent>
            <FieldLabel htmlFor="auto-publish" className="text-sm font-medium">
              Publish automatically
            </FieldLabel>
            <FieldDescription>
              Every new spec version is generated, built and pushed to {registry} with no human
              step. Off by default — publishing to a public registry cannot be undone.
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
        <DialogClose
          render={
            <Button type="button" variant="outline">
              Cancel
            </Button>
          }
        />
        <Button type="submit" disabled={!packageName.trim() || pending}>
          {pending ? (target ? "Saving…" : "Adding…") : target ? "Save changes" : "Add target"}
        </Button>
      </DialogFooter>
    </form>
  );
}
