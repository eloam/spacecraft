import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Field, FieldDescription, FieldError, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { NAME_RULE, isValidResourceName, slugPreview } from "@/lib/resourceName";
import { ApiError } from "@/api/client";
import { useCreateOrganization } from "@/api/queries";

export function CreateOrganizationDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [name, setName] = useState("");
  const navigate = useNavigate();
  const create = useCreateOrganization();
  // Only once something has been typed: an untouched field is not yet wrong.
  const nameInvalid = Boolean(name.trim()) && !isValidResourceName(name);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    create.mutate(
      { name },
      {
        onSuccess: (org) => {
          toast.success(`Created ${org.name}`);
          setName("");
          onOpenChange(false);
          // Straight into it: creating one is only ever a step towards using it, and the
          // switcher can be opened from anywhere, so returning to the list would be wrong.
          navigate(`/orgs/${org.slug}`);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not create the organization."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>New organization</DialogTitle>
            <DialogDescription>
              The address is derived from the name and must be unique across SpaceCraft.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup className="py-4">
            <Field data-invalid={nameInvalid}>
              <FieldLabel htmlFor="org-name">Name</FieldLabel>
              <Input
                id="org-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Acme Corp"
                autoFocus
                aria-invalid={nameInvalid}
              />
              <FieldError errors={nameInvalid ? [{ message: NAME_RULE }] : undefined} />
            </Field>

            <Field>
              <FieldLabel htmlFor="org-slug">Address</FieldLabel>
              {/* Shown while typing rather than after creating: the address is what ends up
                  in every link to this organization, and seeing it is the only way to know
                  whether the name gives the one you wanted. */}
              <Input
                id="org-slug"
                value={slugPreview(name)}
                readOnly
                disabled
                placeholder="acme-corp"
                className="font-mono text-sm"
              />
              <FieldDescription>
                Derived from the name. It can be changed later by renaming.
              </FieldDescription>
            </Field>
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!isValidResourceName(name) || create.isPending}>
              {create.isPending ? "Creating…" : "Create"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
