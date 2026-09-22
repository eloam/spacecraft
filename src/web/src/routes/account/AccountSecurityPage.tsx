import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { KeyRound, Laptop, LogOut, MonitorSmartphone } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import {
  Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle,
} from "@/components/ui/item";
import { Badge } from "@/components/ui/badge";
import { SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ApiError, describeError } from "@/api/client";
import { useChangePassword, useRevokeOtherSessions, useRevokeSession, useSessions } from "@/api/queries";
import { newPasswordField } from "@/lib/fields";

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, "Enter your current password."),
    newPassword: newPasswordField,
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
          let onAField = false;
          if (error instanceof ApiError) {
            const current = error.fieldError("currentPassword");
            const next = error.fieldError("newPassword");
            if (current) form.setError("currentPassword", { message: current });
            if (next) form.setError("newPassword", { message: next });
            onAField = Boolean(current || next);
          }
          // This endpoint is rate limited, so a refusal often arrives with no field errors at
          // all. Without this the button would flip back to "Change password" and say nothing,
          // leaving a failure indistinguishable from a click that never registered.
          if (!onAField) {
            form.setError("root", {
              message: describeError(error, "Could not change your password. Try again."),
            });
          }
        },
      },
    ),
  );

  return (
    <SettingsSection
      icon={<KeyRound />}
      title="Password"
      description="Changing it signs out every other session, but not this one."
    >
        <form onSubmit={onSubmit} className="max-w-md space-y-4" noValidate>
          <FieldGroup>
            {(["currentPassword", "newPassword", "confirmPassword"] as const).map((field) => (
              <Field key={field} data-invalid={!!form.formState.errors[field]}>
                <FieldLabel htmlFor={field}>
                  {field === "currentPassword" ? "Current password"
                    : field === "newPassword" ? "New password" : "Confirm new password"}
                </FieldLabel>
                <Input
                  id={field}
                  type="password"
                  autoComplete={field === "currentPassword" ? "current-password" : "new-password"}
                  aria-invalid={!!form.formState.errors[field]}
                  {...form.register(field)}
                />
                <FieldError errors={[form.formState.errors[field]]} />
              </Field>
            ))}
          </FieldGroup>

          {form.formState.errors.root && (
            <p className="text-sm text-destructive" role="alert">
              {form.formState.errors.root.message}
            </p>
          )}

          <Button type="submit" disabled={change.isPending}>
            {change.isPending ? "Changing…" : "Change password"}
          </Button>
        </form>
    </SettingsSection>
  );
}

function SessionsCard() {
  const sessions = useSessions();
  const revoke = useRevokeSession();
  const revokeOthers = useRevokeOtherSessions();

  const others = sessions.data?.filter((s) => !s.isCurrent) ?? [];

  return (
    <SettingsSection
      icon={<MonitorSmartphone />}
      title="Active sessions"
      description="Each sign-in is a session. Revoke any you do not recognise."
    >
      <div className="space-y-3">
        {/* A failed load used to fall straight through to an empty list: this card of all
            of them would then read as "no other sessions are open", which is a security
            claim the app cannot make when it never heard back. Every revoke below
            invalidates this query, so hasData is what keeps a good list on screen when it
            is the refetch that failed rather than the first load. */}
        <QueryBoundary
          isLoading={sessions.isPending}
          error={sessions.error}
          hasData={sessions.data !== undefined}
          rows={1}
        >
          <ItemGroup>
            {sessions.data?.map((session) => (
              <Item key={session.id} role="listitem" variant="outline" size="sm">
                <ItemMedia variant="icon">
                  <Laptop className="text-muted-foreground" aria-hidden />
                </ItemMedia>
                <ItemContent className="min-w-0">
                  <ItemTitle>
                    Started {new Date(session.startedAt).toLocaleDateString()}
                    {session.isCurrent && <Badge variant="secondary">This device</Badge>}
                  </ItemTitle>
                  <ItemDescription>
                    Last used {new Date(session.lastUsedAt).toLocaleString()}
                  </ItemDescription>
                </ItemContent>
                {!session.isCurrent && (
                  <ItemActions>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() =>
                        revoke.mutate(session.id, {
                          onSuccess: () => toast.success("Session signed out."),
                          // The list is only invalidated on success, so a silent failure
                          // leaves the row exactly as it was — and this card asks people to
                          // revoke sessions they do not recognise. They have to be told the
                          // session is still alive.
                          onError: (error) =>
                            toast.error(describeError(error, "Could not sign out that session.")),
                        })
                      }
                      disabled={revoke.isPending}
                    >
                      <LogOut className="size-4" aria-hidden />
                      <span className="sr-only">Sign out this session</span>
                    </Button>
                  </ItemActions>
                )}
              </Item>
            ))}
          </ItemGroup>

          {others.length > 0 && (
            <Button
              variant="outline"
              size="sm"
              onClick={() =>
                revokeOthers.mutate(undefined, {
                  onSuccess: () => toast.success("Other sessions signed out."),
                  onError: (error) =>
                    toast.error(describeError(error, "Could not sign out the other sessions.")),
                })
              }
              disabled={revokeOthers.isPending}
            >
              Sign out {others.length} other session{others.length > 1 ? "s" : ""}
            </Button>
          )}
        </QueryBoundary>
      </div>
    </SettingsSection>
  );
}

/** Password and sessions together: both answer "who can get into this account". */
export function AccountSecurityPage() {
  return (
    <div className="space-y-6">
      <PasswordCard />
      <SessionsCard />
    </div>
  );
}
