import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { AuthCard } from "@/components/AuthCard";
import { useResetPassword } from "@/api/queries";
import { ApiError } from "@/api/client";

const schema = z
  .object({
    // Mirrors the server's policy so the common mistake is caught before a round trip;
    // the server remains the authority and its message wins when they disagree.
    newPassword: z.string().min(12, "Use at least 12 characters."),
    confirmPassword: z.string(),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    message: "Both passwords must match.",
    path: ["confirmPassword"],
  });

type FormValues = z.infer<typeof schema>;

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const reset = useResetPassword();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { newPassword: "", confirmPassword: "" },
  });

  const onSubmit = form.handleSubmit((values) =>
    reset.mutate({ token, newPassword: values.newPassword }),
  );

  const backToSignIn = (
    <Link to="/login" className="font-medium text-foreground underline underline-offset-4">
      Back to sign in
    </Link>
  );

  // A link opened without its token is a truncated email, not a user error worth a form.
  if (!token) {
    return (
      <AuthCard
        title="Link incomplete"
        description="This reset link is missing its token."
        footer={backToSignIn}
      >
        <p className="text-sm text-muted-foreground">
          Email clients sometimes cut long links in half. Open it from the original message,
          or{" "}
          <Link to="/forgot-password" className="font-medium text-foreground underline underline-offset-4">
            request a new one
          </Link>
          .
        </p>
      </AuthCard>
    );
  }

  if (reset.isSuccess) {
    return (
      <AuthCard title="Password changed" description={reset.data.message} footer={backToSignIn}>
        <p className="text-sm text-muted-foreground">
          Every other session has been signed out. Sign in again with your new password.
        </p>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title="Choose a new password"
      description="Pick something you haven't used elsewhere."
      footer={backToSignIn}
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="newPassword">New password</Label>
          <Input
            id="newPassword"
            type="password"
            autoComplete="new-password"
            autoFocus
            {...form.register("newPassword")}
          />
          {form.formState.errors.newPassword && (
            <p className="text-sm text-destructive">{form.formState.errors.newPassword.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="confirmPassword">Confirm password</Label>
          <Input
            id="confirmPassword"
            type="password"
            autoComplete="new-password"
            {...form.register("confirmPassword")}
          />
          {form.formState.errors.confirmPassword && (
            <p className="text-sm text-destructive">
              {form.formState.errors.confirmPassword.message}
            </p>
          )}
        </div>

        {reset.error && (
          <p className="text-sm text-destructive" role="alert">
            {reset.error instanceof ApiError
              ? reset.error.detail
              : "Something went wrong. Try again."}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={reset.isPending}>
          {reset.isPending ? "Saving…" : "Set new password"}
        </Button>
      </form>
    </AuthCard>
  );
}
