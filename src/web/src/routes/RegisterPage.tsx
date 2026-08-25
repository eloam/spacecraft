import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { AuthCard } from "@/components/AuthCard";
import { useRegister } from "@/api/queries";
import { ApiError } from "@/api/client";

const schema = z.object({
  displayName: z.string().trim().max(80).optional(),
  email: z.string().email("Enter a valid email address."),
  // Mirrors the API's Identity policy: length only, no composition rules.
  password: z.string().min(12, "Use at least 12 characters."),
});

type FormValues = z.infer<typeof schema>;

export function RegisterPage() {
  const register = useRegister();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { displayName: "", email: "", password: "" },
  });

  const onSubmit = form.handleSubmit((values) =>
    register.mutate({ ...values, displayName: values.displayName || undefined }),
  );

  const apiError = register.error instanceof ApiError ? register.error : undefined;

  return (
    <AuthCard
      title="Create your account"
      description="One OpenAPI document in, SDKs everywhere out."
      footer={
        <>
          Already registered?{" "}
          <Link to="/login" className="font-medium text-foreground underline underline-offset-4">
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="displayName">Name</Label>
          <Input id="displayName" autoComplete="name" {...form.register("displayName")} />
        </div>

        <div className="space-y-2">
          <Label htmlFor="email">Email</Label>
          <Input id="email" type="email" autoComplete="email" {...form.register("email")} />
          {(form.formState.errors.email || apiError?.fieldError("email")) && (
            <p className="text-sm text-destructive">
              {form.formState.errors.email?.message ?? apiError?.fieldError("email")}
            </p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="password">Password</Label>
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            {...form.register("password")}
          />
          {(form.formState.errors.password || apiError?.fieldError("password")) && (
            <p className="text-sm text-destructive">
              {form.formState.errors.password?.message ?? apiError?.fieldError("password")}
            </p>
          )}
        </div>

        <Button type="submit" className="w-full" disabled={register.isPending}>
          {register.isPending ? "Creating account…" : "Create account"}
        </Button>
      </form>
    </AuthCard>
  );
}
