import { User } from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { SettingsSection } from "@/components/SettingsSections";
import { ApiError } from "@/api/client";
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
      {
        onSuccess: () => toast.success("Profile updated."),
        // The field keeps the typed name whether the save worked or not, so without this a
        // failure looks exactly like a success.
        onError: (error) =>
          toast.error(
            error instanceof ApiError ? error.detail : "Could not save your profile.",
          ),
      },
    ),
  );

  return (
    <SettingsSection
      icon={<User />}
      title="Profile"
      description="How you appear across SpaceCraft."
    >
        <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
          <FieldGroup>
            <Field data-invalid={!!form.formState.errors.displayName}>
              <FieldLabel htmlFor="displayName">Display name</FieldLabel>
              <Input
                id="displayName"
                placeholder="Not set"
                aria-invalid={!!form.formState.errors.displayName}
                {...form.register("displayName")}
              />
              <FieldError errors={[form.formState.errors.displayName]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="email">Email</FieldLabel>
              {/* Changing an address needs a verification round trip to prove the new one is
                  reachable; until that exists, showing it read-only is honest. */}
              <Input id="email" value={email} readOnly disabled />
              <FieldDescription>
                Contact support to change your email address.
              </FieldDescription>
            </Field>
          </FieldGroup>

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
    </SettingsSection>
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
