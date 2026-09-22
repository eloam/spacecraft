import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { AuthCard } from "@/components/AuthCard";
import { useRegister } from "@/api/queries";
import { useReturnQuery } from "@/lib/returnTo";
import { ApiError, describeError } from "@/api/client";
import { displayNameField, emailField, newPasswordField } from "@/lib/fields";

const schema = z.object({
  displayName: displayNameField.optional(),
  email: emailField,
  password: newPasswordField,
});

type FormValues = z.infer<typeof schema>;

export function RegisterPage() {
  const register = useRegister();
  const returnQuery = useReturnQuery();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { displayName: "", email: "", password: "" },
  });

  const onSubmit = form.handleSubmit((values) =>
    register.mutate(
      { ...values, displayName: values.displayName || undefined },
      {
        onError: (error) => {
          // On the field, in the form's own state, so retyping the address clears the
          // complaint. Derived from the mutation's error at render time instead, the field
          // stayed red until the next submit whatever was typed into it.
          if (error instanceof ApiError) {
            const email = error.fieldError("email");
            const password = error.fieldError("password");
            if (email) form.setError("email", { message: email });
            if (password) form.setError("password", { message: password });
            if (email || password) return;
          }
          // Only the two field-mapped complaints reach a Field. A 409, a 500, a dropped
          // connection or a validation error keyed to anything else would otherwise land
          // nowhere and leave the button flicking back to "Create account" as the only sign
          // the submit happened.
          form.setError("root", {
            message: describeError(error, "Something went wrong. Try again."),
          });
        },
      },
    ),
  );

  return (
    <>
      <title>Create your account — SpaceCraft</title>

      <AuthCard
        title="Create your account"
        description="One OpenAPI document in, SDKs everywhere out."
        footer={
          <>
            Already registered?{" "}
            <Link
              to={`/login${returnQuery}`}
              className="font-medium text-foreground underline underline-offset-4"
            >
              Sign in
            </Link>
          </>
        }
      >
        <form onSubmit={onSubmit} className="space-y-4" noValidate>
          <FieldGroup>
            <Field data-invalid={!!form.formState.errors.displayName}>
              <FieldLabel htmlFor="displayName">Name</FieldLabel>
              {/* The length rule is enforced but was never shown: a name over the limit failed
                  validation, so no request was made, so the error outlet below had nothing to
                  report — the button simply did nothing. */}
              <Input
                id="displayName"
                autoComplete="name"
                aria-invalid={!!form.formState.errors.displayName}
                {...form.register("displayName")}
              />
              <FieldError errors={[form.formState.errors.displayName]} />
            </Field>

            <Field data-invalid={!!form.formState.errors.email}>
              <FieldLabel htmlFor="email">Email</FieldLabel>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                aria-invalid={!!form.formState.errors.email}
                {...form.register("email")}
              />
              <FieldError errors={[form.formState.errors.email]} />
            </Field>

            <Field data-invalid={!!form.formState.errors.password}>
              <FieldLabel htmlFor="password">Password</FieldLabel>
              <Input
                id="password"
                type="password"
                autoComplete="new-password"
                aria-invalid={!!form.formState.errors.password}
                {...form.register("password")}
              />
              <FieldError errors={[form.formState.errors.password]} />
            </Field>
          </FieldGroup>

          {form.formState.errors.root && (
            <p className="text-sm text-destructive" role="alert">
              {form.formState.errors.root.message}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={register.isPending}>
            {register.isPending ? "Creating account…" : "Create account"}
          </Button>
        </form>
      </AuthCard>
    </>
  );
}
