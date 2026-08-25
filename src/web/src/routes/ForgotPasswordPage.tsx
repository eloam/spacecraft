import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { AuthCard } from "@/components/AuthCard";
import { useForgotPassword } from "@/api/queries";
import { ApiError } from "@/api/client";

const schema = z.object({
  email: z.string().email("Enter a valid email address."),
});

type FormValues = z.infer<typeof schema>;

export function ForgotPasswordPage() {
  const forgot = useForgotPassword();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "" },
  });

  const onSubmit = form.handleSubmit((values) => forgot.mutate(values));

  const backToSignIn = (
    <Link to="/login" className="font-medium text-foreground underline underline-offset-4">
      Back to sign in
    </Link>
  );

  // The confirmation says nothing about whether the address is registered — the same
  // reasoning as the API response it mirrors.
  if (forgot.isSuccess) {
    return (
      <AuthCard
        title="Check your inbox"
        description={forgot.data.message}
        footer={backToSignIn}
      >
        <p className="text-sm text-muted-foreground">
          The link works once and expires in an hour. If it doesn't arrive, check your spam
          folder before requesting another.
        </p>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title="Reset your password"
      description="We'll email you a link to choose a new one."
      footer={backToSignIn}
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="email">Email</Label>
          <Input id="email" type="email" autoComplete="email" autoFocus {...form.register("email")} />
          {form.formState.errors.email && (
            <p className="text-sm text-destructive">{form.formState.errors.email.message}</p>
          )}
        </div>

        {forgot.error && (
          <p className="text-sm text-destructive" role="alert">
            {forgot.error instanceof ApiError
              ? forgot.error.detail
              : "Something went wrong. Try again."}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={forgot.isPending}>
          {forgot.isPending ? "Sending…" : "Send reset link"}
        </Button>
      </form>
    </AuthCard>
  );
}
