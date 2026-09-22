import { User } from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Separator } from "@/components/ui/separator";
import { SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { describeError } from "@/api/client";
import { useAccount, useUpdateProfile } from "@/api/queries";
import { displayNameField } from "@/lib/fields";
import { formatDate, formatDateTime } from "@/lib/format";

const profileSchema = z.object({
  displayName: displayNameField,
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
      { displayName: values.displayName || null },
      {
        onSuccess: (account) => {
          // The saved name is the new baseline. Judged against the value the form opened
          // with, Save stayed enabled after a save went through, and typing the original
          // name back was never dirty, so it could not be restored without a reload.
          form.reset({ displayName: account.displayName ?? "" });
          toast.success("Profile updated.");
        },
        // The field keeps the typed name whether the save worked or not, so without this a
        // failure looks exactly like a success.
        onError: (error) => toast.error(describeError(error, "Could not save your profile.")),
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
              <dd>
                <time dateTime={createdAt}>{formatDate(createdAt)}</time>
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Last sign-in</dt>
              <dd>
                {lastLoginAt ? (
                  <time dateTime={lastLoginAt}>{formatDateTime(lastLoginAt)}</time>
                ) : (
                  "—"
                )}
              </dd>
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

  return (
    <>
      <title>Profile — SpaceCraft</title>

      <QueryBoundary
        isLoading={account.isPending}
        error={account.error}
        hasData={account.data !== undefined}
      >
        {account.data && (
          <ProfileCard
            displayName={account.data.displayName}
            email={account.data.email}
            createdAt={account.data.createdAt}
            lastLoginAt={account.data.lastLoginAt}
          />
        )}
      </QueryBoundary>
    </>
  );
}
