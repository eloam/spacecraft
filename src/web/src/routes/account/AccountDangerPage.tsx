import { useState, type FormEvent } from "react";
import { CircleAlert, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { DangerSection } from "@/components/SettingsSections";
import { Panel, PanelLine, PanelSection } from "@/components/Panel";
import { QueryBoundary } from "@/components/QueryBoundary";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { ApiError, describeError } from "@/api/client";
import { useAccount, useDeleteAccount, useDeletionBlockers } from "@/api/queries";
import { plural } from "@/lib/format";

function DangerZone({ organizationCount }: { organizationCount: number }) {
  const blockers = useDeletionBlockers();

  const blocked = (blockers.data?.length ?? 0) > 0;
  // Unknown is not none: while the check has not answered, or answered with a failure, the
  // button stays off rather than offer a delete the server would refuse.
  const unchecked = blockers.isPending || blockers.isError;

  return (
    <DangerSection
      icon={<Trash2 />}
      title="Delete account"
      description={
        <>
          Permanently removes your account and your membership of{" "}
          {plural(organizationCount, "organization")}. This cannot be undone.
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

        {blockers.isError && (
          <p className="text-sm text-destructive" role="alert">
            {describeError(blockers.error, "Could not check what stands in the way.")}
          </p>
        )}

        <p className="text-sm text-muted-foreground">
          SDKs you have already published stay in their registries — SpaceCraft cannot unpublish
          them on your behalf.
        </p>

        <Dialog>
          <DialogTrigger
            render={
              <Button variant="destructive" disabled={blocked || unchecked}>
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

  // The mutation's own error, read where it is shown and cleared by `reset()` on the next
  // keystroke — not a copy of it kept in state beside it.
  const error = remove.error
    ? (remove.error instanceof ApiError && remove.error.fieldError("password")) ||
      describeError(remove.error, "Could not delete the account.")
    : null;

  function submit(event: FormEvent) {
    event.preventDefault();
    remove.mutate({ password });
  }

  return (
    <form onSubmit={submit} noValidate>
      <DialogHeader>
        <DialogTitle>Delete your account?</DialogTitle>
        <DialogDescription>
          This removes your account permanently. Enter your password to confirm.
        </DialogDescription>
      </DialogHeader>

      <FieldGroup className="py-4">
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
              // Only once it has failed: a reset while the delete is in flight would detach
              // this form from its outcome.
              if (remove.isError) remove.reset();
            }}
          />
          <FieldError>{error}</FieldError>
        </Field>
      </FieldGroup>

      <DialogFooter>
        <DialogClose
          render={
            <Button variant="outline" type="button">
              Cancel
            </Button>
          }
        />
        <Button type="submit" variant="destructive" disabled={!password || remove.isPending}>
          {remove.isPending ? "Deleting…" : "Delete permanently"}
        </Button>
      </DialogFooter>
    </form>
  );
}

export function AccountDangerPage() {
  const account = useAccount();

  return (
    <>
      <title>Danger zone — SpaceCraft</title>

      <QueryBoundary
        isLoading={account.isPending}
        error={account.error}
        hasData={account.data !== undefined}
      >
        {account.data && <DangerZone organizationCount={account.data.organizationCount} />}
      </QueryBoundary>
    </>
  );
}
