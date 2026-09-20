import { useState } from "react";
import { CircleAlert, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldLabel } from "@/components/ui/field";
import { Skeleton } from "@/components/ui/skeleton";
import { DangerSection } from "@/components/SettingsSections";
import { Panel, PanelLine, PanelSection } from "@/components/Panel";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle, DialogTrigger,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAccount, useDeleteAccount, useDeletionBlockers } from "@/api/queries";

function DangerZone({ organizationCount }: { organizationCount: number }) {
  const blockers = useDeletionBlockers();

  const blocked = (blockers.data?.length ?? 0) > 0;

  return (
    <DangerSection
      icon={<Trash2 />}
      title="Delete account"
      description={
        <>
          Permanently removes your account and your membership of {organizationCount}{" "}
          organization{organizationCount === 1 ? "" : "s"}. This cannot be undone.
        </>
      }
    >
      <div className="space-y-3">
        {blocked && (
          // The same ledger the delete dialogs use: a red frame around a grey icon put the
          // warning colour on the panel rather than on the thing being warned about, and
          // this list is not a warning — it is the list of what stands in the way.
          <Panel>
            <PanelSection label="Hand these over first" tone="destructive">
              {blockers.data?.map((b) => (
                // A blocker, not a removal: these organizations are not going anywhere, they
                // are what has to move before you can.
                <PanelLine key={b.organizationName} icon={CircleAlert} tone="destructive">
                  {b.organizationName} — {b.reason.toLowerCase()}
                </PanelLine>
              ))}
            </PanelSection>

            <p className="text-muted-foreground">
              Add another owner, or delete the organization, then come back.
            </p>
          </Panel>
        )}

        <p className="text-sm text-muted-foreground">
          SDKs you have already published stay in their registries — SpaceCraft cannot
          unpublish them on your behalf.
        </p>

        <Dialog>
          <DialogTrigger
            render={
              <Button variant="destructive" disabled={blocked || blockers.isPending}>
                <Trash2 className="size-4" aria-hidden />
                Delete my account
              </Button>
            }
          />
          <DialogContent>
            <DeleteAccountForm />
          </DialogContent>
        </Dialog>
      </div>
    </DangerSection>
  );
}

/**
 * The password lives in a child of the content, which Base UI unmounts once the dialog has
 * closed, so it is discarded on every close and each opening starts blank. Held by DangerZone
 * instead, which stays mounted for the life of the page, a cancelled attempt left it in state
 * and pre-filled the next time the dialog opened.
 */
function DeleteAccountForm() {
  const remove = useDeleteAccount();
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const onDelete = () =>
    remove.mutate(
      { password },
      {
        onError: (e) =>
          setError(
            e instanceof ApiError
              ? e.errors?.Password?.join(" ") ?? e.detail
              : "Could not delete the account.",
          ),
      },
    );

  return (
    <>
      <DialogHeader>
        <DialogTitle>Delete your account?</DialogTitle>
        <DialogDescription>
          This removes your account permanently. Enter your password to confirm.
        </DialogDescription>
      </DialogHeader>

      <Field data-invalid={!!error}>
        <FieldLabel htmlFor="confirm-password">Password</FieldLabel>
        <Input
          id="confirm-password"
          type="password"
          autoComplete="current-password"
          value={password}
          aria-invalid={!!error}
          onChange={(e) => {
            setPassword(e.target.value);
            setError(null);
          }}
        />
        <FieldError>{error}</FieldError>
      </Field>

      <DialogFooter>
        <DialogClose render={<Button variant="outline">Cancel</Button>} />
        <Button variant="destructive" onClick={onDelete} disabled={!password || remove.isPending}>
          {remove.isPending ? "Deleting…" : "Delete permanently"}
        </Button>
      </DialogFooter>
    </>
  );
}

export function AccountDangerPage() {
  const account = useAccount();

  if (account.isPending) {
    return <Skeleton className="h-48 w-full" />;
  }

  return <DangerZone organizationCount={account.data?.organizationCount ?? 0} />;
}
