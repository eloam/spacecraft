import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { AuthCard } from "@/components/AuthCard";
import { useResetPassword } from "@/api/queries";
import { describeError } from "@/api/client";
import { newPasswordField } from "@/lib/fields";

const schema = z
  .object({
    newPassword: newPasswordField,
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
      <>
        <title>Reset password — SpaceCraft</title>

        <AuthCard
          title="Link incomplete"
          description="This reset link is missing its token."
          footer={backToSignIn}
        >
          <p className="text-sm text-muted-foreground">
            Email clients sometimes cut long links in half. Open it from the original message, or{" "}
            <Link
              to="/forgot-password"
              className="font-medium text-foreground underline underline-offset-4"
            >
              request a new one
            </Link>
            .
          </p>
        </AuthCard>
      </>
    );
  }

  if (reset.isSuccess) {
    return (
      <>
        <title>Reset password — SpaceCraft</title>

        <AuthCard title="Password changed" description={reset.data.message} footer={backToSignIn}>
          <p className="text-sm text-muted-foreground">
            Every other session has been signed out. Sign in again with your new password.
          </p>
        </AuthCard>
      </>
    );
  }

  return (
    <>
      <title>Reset password — SpaceCraft</title>

      <AuthCard
        title="Choose a new password"
        description="Pick something you haven't used elsewhere."
        footer={backToSignIn}
      >
        <form onSubmit={onSubmit} className="space-y-4" noValidate>
          <FieldGroup>
            <Field data-invalid={!!form.formState.errors.newPassword}>
              <FieldLabel htmlFor="newPassword">New password</FieldLabel>
              <Input
                id="newPassword"
                type="password"
                autoComplete="new-password"
                autoFocus
                aria-invalid={!!form.formState.errors.newPassword}
                {...form.register("newPassword")}
              />
              <FieldError errors={[form.formState.errors.newPassword]} />
            </Field>

            <Field data-invalid={!!form.formState.errors.confirmPassword}>
              <FieldLabel htmlFor="confirmPassword">Confirm password</FieldLabel>
              <Input
                id="confirmPassword"
                type="password"
                autoComplete="new-password"
                aria-invalid={!!form.formState.errors.confirmPassword}
                {...form.register("confirmPassword")}
              />
              <FieldError errors={[form.formState.errors.confirmPassword]} />
            </Field>
          </FieldGroup>

          {reset.error && (
            <p className="text-sm text-destructive" role="alert">
              {describeError(reset.error, "Something went wrong. Try again.")}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={reset.isPending}>
            {reset.isPending ? "Saving…" : "Set new password"}
          </Button>
        </form>
      </AuthCard>
    </>
  );
}
