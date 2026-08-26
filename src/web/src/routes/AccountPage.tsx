import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Laptop, LogOut, ShieldAlert, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle, DialogTrigger,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import {
  useAccount, useChangePassword, useDeleteAccount, useDeletionBlockers,
  useRevokeOtherSessions, useRevokeSession, useSessions, useUpdateProfile,
} from "@/api/queries";

export function AccountPage() {
  const account = useAccount();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Account</h1>
        <p className="text-sm text-muted-foreground">
          Your profile, password and active sessions.
        </p>
      </div>

      {account.isPending ? (
        <Skeleton className="h-40 w-full" />
      ) : account.isError ? (
        <p className="text-sm text-destructive">Could not load your account.</p>
      ) : (
        <>
          <ProfileCard displayName={account.data.displayName} email={account.data.email}
                       createdAt={account.data.createdAt} lastLoginAt={account.data.lastLoginAt} />
          <PasswordCard />
          <SessionsCard />
          <DangerZone organizationCount={account.data.organizationCount} />
        </>
      )}
    </div>
  );
}

const profileSchema = z.object({
  displayName: z.string().max(100, "Use 100 characters or fewer."),
});

function ProfileCard({
  displayName, email, createdAt, lastLoginAt,
}: {
  displayName: string | null;
  email: string;
  createdAt: string;
  lastLoginAt: string | null;
}) {
  const update = useUpdateProfile();
  const form = useForm<z.infer<typeof profileSchema>>({
    resolver: zodResolver(profileSchema),
    defaultValues: { displayName: displayName ?? "" },
  });

  const onSubmit = form.handleSubmit((values) =>
    update.mutate(
      { displayName: values.displayName.trim() || null },
      { onSuccess: () => toast.success("Profile updated.") },
    ),
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>Profile</CardTitle>
        <CardDescription>How you appear across SpaceCraft.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="displayName">Display name</Label>
            <Input id="displayName" placeholder="Not set" {...form.register("displayName")} />
            {form.formState.errors.displayName && (
              <p className="text-sm text-destructive">{form.formState.errors.displayName.message}</p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            {/* Changing an address needs a verification round trip to prove the new one is
                reachable; until that exists, showing it read-only is honest. */}
            <Input id="email" value={email} readOnly disabled />
            <p className="text-xs text-muted-foreground">
              Contact support to change your email address.
            </p>
          </div>

          <Separator />

          <dl className="grid grid-cols-2 gap-3 text-sm">
            <div>
              <dt className="text-muted-foreground">Member since</dt>
              <dd>{new Date(createdAt).toLocaleDateString()}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Last sign-in</dt>
              <dd>{lastLoginAt ? new Date(lastLoginAt).toLocaleString() : "—"}</dd>
            </div>
          </dl>

          <Button type="submit" disabled={update.isPending || !form.formState.isDirty}>
            {update.isPending ? "Saving…" : "Save changes"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, "Enter your current password."),
    newPassword: z.string().min(12, "Use at least 12 characters."),
    confirmPassword: z.string(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    message: "Both passwords must match.",
    path: ["confirmPassword"],
  });

function PasswordCard() {
  const change = useChangePassword();
  const form = useForm<z.infer<typeof passwordSchema>>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  const onSubmit = form.handleSubmit((values) =>
    change.mutate(
      { currentPassword: values.currentPassword, newPassword: values.newPassword },
      {
        onSuccess: () => {
          form.reset();
          toast.success("Password changed. Other sessions have been signed out.");
        },
        onError: (error) => {
          // The server distinguishes a wrong current password from a weak new one; putting
          // the message on the right field is the difference between useful and baffling.
          if (error instanceof ApiError && error.errors) {
            for (const [field, messages] of Object.entries(error.errors)) {
              const key = field.charAt(0).toLowerCase() + field.slice(1);
              if (key === "currentPassword" || key === "newPassword") {
                form.setError(key, { message: messages.join(" ") });
              }
            }
          }
        },
      },
    ),
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>Password</CardTitle>
        <CardDescription>
          Changing it signs out every other session, but not this one.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
          {(["currentPassword", "newPassword", "confirmPassword"] as const).map((field) => (
            <div key={field} className="space-y-2">
              <Label htmlFor={field}>
                {field === "currentPassword" ? "Current password"
                  : field === "newPassword" ? "New password" : "Confirm new password"}
              </Label>
              <Input
                id={field}
                type="password"
                autoComplete={field === "currentPassword" ? "current-password" : "new-password"}
                {...form.register(field)}
              />
              {form.formState.errors[field] && (
                <p className="text-sm text-destructive">{form.formState.errors[field]?.message}</p>
              )}
            </div>
          ))}

          <Button type="submit" disabled={change.isPending}>
            {change.isPending ? "Changing…" : "Change password"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function SessionsCard() {
  const sessions = useSessions();
  const revoke = useRevokeSession();
  const revokeOthers = useRevokeOtherSessions();

  const others = sessions.data?.filter((s) => !s.isCurrent) ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Active sessions</CardTitle>
        <CardDescription>
          Each sign-in is a session. Revoke any you do not recognise.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {sessions.isPending ? (
          <Skeleton className="h-16 w-full" />
        ) : (
          <>
            <ul className="divide-y rounded-lg border">
              {sessions.data?.map((session) => (
                <li key={session.id} className="flex items-center gap-3 p-3">
                  <Laptop className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                  <div className="min-w-0 flex-1 text-sm">
                    <div className="flex items-center gap-2">
                      <span className="font-medium">
                        Started {new Date(session.startedAt).toLocaleDateString()}
                      </span>
                      {session.isCurrent && <Badge variant="secondary">This device</Badge>}
                    </div>
                    <p className="text-xs text-muted-foreground">
                      Last used {new Date(session.lastUsedAt).toLocaleString()}
                    </p>
                  </div>
                  {!session.isCurrent && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() =>
                        revoke.mutate(session.id, {
                          onSuccess: () => toast.success("Session signed out."),
                        })
                      }
                      disabled={revoke.isPending}
                    >
                      <LogOut className="size-4" aria-hidden />
                      <span className="sr-only">Sign out this session</span>
                    </Button>
                  )}
                </li>
              ))}
            </ul>

            {others.length > 0 && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  revokeOthers.mutate(undefined, {
                    onSuccess: () => toast.success("Other sessions signed out."),
                  })
                }
                disabled={revokeOthers.isPending}
              >
                Sign out {others.length} other session{others.length > 1 ? "s" : ""}
              </Button>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

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
