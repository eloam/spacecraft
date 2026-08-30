import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Laptop, LogOut } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import {
  Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle,
} from "@/components/ui/item";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ApiError } from "@/api/client";
import { useChangePassword, useRevokeOtherSessions, useRevokeSession, useSessions } from "@/api/queries";

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

/** Password and sessions together: both answer "who can get into this account". */
export function AccountSecurityPage() {
  return (
    <div className="space-y-6">
      <PasswordCard />
      <SessionsCard />
    </div>
  );
}
