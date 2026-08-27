import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useAccount, useUpdateProfile } from "@/api/queries";

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


export function AccountProfilePage() {
  const account = useAccount();

  if (account.isPending) {
    return <Skeleton className="h-64 w-full" />;
  }

  if (account.isError || !account.data) {
    return <p className="text-sm text-destructive">Could not load your account.</p>;
  }

  return (
    <ProfileCard
      displayName={account.data.displayName}
      email={account.data.email}
      createdAt={account.data.createdAt}
      lastLoginAt={account.data.lastLoginAt}
    />
  );
}
