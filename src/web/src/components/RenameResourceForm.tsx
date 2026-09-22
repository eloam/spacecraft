import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { ApiError, describeError } from "@/api/client";
import { resourceNameField, slugPreview } from "@/lib/resourceName";

const schema = z.object({ name: resourceNameField });

type FormValues = z.infer<typeof schema>;

/**
 * The General section of a settings page: the name, and the slug it will become.
 *
 * Organizations and projects are named by the same rule and renamed the same way, so the
 * two settings pages carried this form twice, one word apart. The form, its fields and what
 * goes wrong on a field live here; what a save means — the toast, and following the slug
 * when it moves — is the page's.
 */
export function RenameResourceForm({
  resourceKind,
  name,
  slug,
  isPending,
  onSubmit,
}: {
  resourceKind: "organization" | "project";
  /** The name as the server holds it. */
  name: string;
  /** The slug as the server holds it. */
  slug: string;
  isPending: boolean;
  /** Saves the name. Rejects with the API's error when it was refused. */
  onSubmit: (values: FormValues) => Promise<void>;
}) {
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    // `values` re-syncs a refetched name into the form without an effect, and keepDirtyValues
    // keeps a name being typed from being overwritten by that refetch.
    values: { name },
    resetOptions: { keepDirtyValues: true },
  });

  const typed = useWatch({ control: form.control, name: "name" });
  const nextSlug = slugPreview(typed) || slug;
  const slugWillChange = nextSlug !== slug;

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values);
    } catch (error) {
      // On the field, in the form's own state, so retyping the name clears the complaint.
      // Anything the API did not key to the field — a slug already taken, a dropped
      // connection — has no field to land on and is said in a toast.
      const message = error instanceof ApiError ? error.fieldError("name") : undefined;
      if (message) {
        form.setError("name", { message });
      } else {
        toast.error(describeError(error, `Could not rename the ${resourceKind}.`));
      }
      return;
    }

    // The saved name is the new baseline, so isDirty is judged against what the server now
    // holds — the trimmed value, which is what was sent. `reset` merges resetOptions and the
    // field just saved is the dirty one, so keepDirtyValues is switched off here: the saved
    // value replaces what was typed and the field is clean again for the next server sync.
    form.reset(values, { keepDirtyValues: false });
  });

  return (
    <form onSubmit={submit} className="max-w-md space-y-4" noValidate>
      <FieldGroup>
        <Field data-invalid={!!form.formState.errors.name}>
          <FieldLabel htmlFor="name">Name</FieldLabel>
          <Input id="name" aria-invalid={!!form.formState.errors.name} {...form.register("name")} />
          <FieldError errors={[form.formState.errors.name]} />
        </Field>

        <Field>
          <FieldLabel htmlFor="slug">Slug</FieldLabel>
          <Input id="slug" value={nextSlug} readOnly disabled className="font-mono text-sm" />
          {slugWillChange ? (
            // Renaming moves the slug, and the slug is the URL. Saying so before the click is
            // the difference between a rename and a broken link someone discovers later.
            <FieldDescription className="text-xs text-destructive">
              Renaming changes the slug. Existing links to <code className="font-mono">{slug}</code>{" "}
              will stop working.
            </FieldDescription>
          ) : (
            <FieldDescription className="text-xs">
              Derived from the name, and used in this {resourceKind}&apos;s URL.
            </FieldDescription>
          )}
        </Field>
      </FieldGroup>

      <Button type="submit" disabled={isPending || !form.formState.isDirty}>
        {isPending ? "Saving…" : "Save changes"}
      </Button>
    </form>
  );
}
