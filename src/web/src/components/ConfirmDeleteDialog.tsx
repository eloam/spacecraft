import { useState } from "react";
import { Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldLabel } from "@/components/ui/field";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";

/**
 * Confirmation for a delete that cannot be undone.
 *
 * When `confirmText` is given, the exact words have to be typed. That is reserved for
 * deletes whose blast radius is wide — an organization takes every project with it — because
 * asking for it everywhere trains people to type without reading.
 */
export function ConfirmDeleteDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmText,
  consequences,
  details,
  isPending,
  error,
  onConfirm,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmText?: string;
  consequences?: string[];
  /** Rendered under the consequences, for what a list of names says better than a count. */
  details?: React.ReactNode;
  isPending?: boolean;
  error?: string | null;
  onConfirm: () => void;
}) {
  const [typed, setTyped] = useState("");
  const confirmed = !confirmText || typed.trim() === confirmText;

  function change(next: boolean) {
    if (!next) {
      setTyped("");
    }
    onOpenChange(next);
  }

  return (
    <Dialog open={open} onOpenChange={change}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        {consequences && consequences.length > 0 && (
          <ul className="space-y-1 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
            {consequences.map((line) => (
              <li key={line} className="text-muted-foreground">
                {line}
              </li>
            ))}
          </ul>
        )}

        {details}

        {confirmText && (
          <Field>
            <FieldLabel htmlFor="confirm-text">
              Type{" "}
              {/* Monospaced on a tinted ground, the way slugs and package names are shown
                  elsewhere. A name like "Load Test 2" contains spaces, and in running text
                  there is no telling where it starts or whether they are part of it. */}
              <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[13px] font-medium text-foreground">
                {confirmText}
              </code>{" "}
              to confirm
            </FieldLabel>
            <Input
              id="confirm-text"
              value={typed}
              autoComplete="off"
              autoCorrect="off"
              autoCapitalize="off"
              spellCheck={false}
              className="font-mono text-sm"
              onChange={(e) => setTyped(e.target.value)}
            />
          </Field>
        )}

        {error && <p className="text-sm text-destructive">{error}</p>}

        <DialogFooter>
          <DialogClose render={<Button variant="outline">Cancel</Button>} />
          <Button
            variant="destructive"
            onClick={onConfirm}
            disabled={!confirmed || isPending}
          >
            <Trash2 className="size-4" aria-hidden />
            {isPending ? "Deleting…" : "Delete"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
