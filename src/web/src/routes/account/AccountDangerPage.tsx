import { useState } from "react";
import { ShieldAlert, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle, DialogTrigger,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useAccount, useDeleteAccount, useDeletionBlockers } from "@/api/queries";

function DangerZone({ organizationCount }: { organizationCount: number }) {
  const blockers = useDeletionBlockers();
  const remove = useDeleteAccount();
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const blocked = (blockers.data?.length ?? 0) > 0;

  const onDelete = () =>
    remove.mutate(
      { password },
      {
        onSuccess: () => {
          // The store is cleared by the mutation; a full reload drops any in-memory state
          // that a client-side route change would leave behind.
          window.location.href = "/login";
        },
        onError: (e) =>
          setError(
            e instanceof ApiError
              ? e.errors?.Password?.join(" ") ?? e.detail
              : "Could not delete the account.",
          ),
      },
    );

  return (
    <Card className="border-destructive/40">
      <CardHeader>
        <CardTitle className="text-destructive">Delete account</CardTitle>
        <CardDescription>
          Permanently removes your account and your membership of{" "}
          {organizationCount} organization{organizationCount === 1 ? "" : "s"}. This cannot be
          undone.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {blocked && (
          <div className="flex gap-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3">
            <ShieldAlert className="size-4 shrink-0 text-destructive" aria-hidden />
            <div className="text-sm">
              <p className="font-medium">Hand these over first</p>
              <ul className="mt-1 space-y-0.5 text-muted-foreground">
                {blockers.data?.map((b) => (
                  <li key={b.organizationName}>
                    {b.organizationName} — {b.reason.toLowerCase()}
                  </li>
                ))}
              </ul>
              <p className="mt-2 text-muted-foreground">
                Add another owner, or delete the organization, then come back.
              </p>
            </div>
          </div>
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
            <DialogHeader>
              <DialogTitle>Delete your account?</DialogTitle>
              <DialogDescription>
                This removes your account permanently. Enter your password to confirm.
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-2">
              <Label htmlFor="confirm-password">Password</Label>
              <Input
                id="confirm-password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  setError(null);
                }}
              />
              {error && <p className="text-sm text-destructive">{error}</p>}
            </div>

            <DialogFooter>
              <DialogClose render={<Button variant="outline">Cancel</Button>} />
              <Button variant="destructive" onClick={onDelete} disabled={!password || remove.isPending}>
                {remove.isPending ? "Deleting…" : "Delete permanently"}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </CardContent>
    </Card>
  );
}

export function AccountDangerPage() {
  const account = useAccount();

  if (account.isPending) {
    return <Skeleton className="h-48 w-full" />;
  }

  return <DangerZone organizationCount={account.data?.organizationCount ?? 0} />;
}
