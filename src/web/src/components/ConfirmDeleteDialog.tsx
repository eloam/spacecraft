import type { ReactNode } from "react";
import { useRef, useState } from "react";
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
  confirmLabel = "Delete",
  confirmIcon = <Trash2 className="size-4" aria-hidden />,
  consequences,
  reassurances,
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
  /**
   * What the confirming button says. Defaults to Delete, which is wrong for the two dialogs
   * that are not deletes: leaving an organization offered a red "Delete" under the title
   * "Leave this organization?".
   */
  confirmLabel?: string;
  /** Follows the label: a trash can under "Leave this organization?" reads as deleting it. */
  confirmIcon?: ReactNode;
  consequences?: string[];
  /**
   * What survives the delete. Kept out of the red box on purpose: an unheaded list inside a
   * destructive frame reads as things being destroyed, and "Packages already published stay
   * in their registries" said the opposite of what it looked like.
   */
  reassurances?: string[];
  /** Rendered under the consequences, for what a list of names says better than a count. */
  details?: React.ReactNode;
  isPending?: boolean;
  error?: string | null;
  onConfirm: () => void;
}) {
  const [typed, setTyped] = useState("");
  const confirmed = !confirmText || typed.trim() === confirmText;

  // Callers null the row they were deleting in onOpenChange, but this dialog stays mounted
  // for the length of its close animation — so the title faded out reading "Delete spec
  // #undefined?" and the typed-name field vanished mid-fade. Hold the last real values and
  // render those while it leaves.
  const latched = useRef({ title, description, confirmText });
  if (open) {
    latched.current = { title, description, confirmText };
  }
  const shown = open ? { title, description, confirmText } : latched.current;

  // Base UI focuses the first focusable child, which here is whatever `details` renders —
  // on the credentials dialog that is a link out of the page, so Enter on open navigated
  // away. Point it at the confirmation field, or at Cancel when there is none.
  const inputRef = useRef<HTMLInputElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  function change(next: boolean) {
    if (!next) {
      setTyped("");
    }
    onOpenChange(next);
  }

  return (
    <Dialog open={open} onOpenChange={change}>
      {/* Resolved when focus is placed, not when the element is described: a bare ref
          whose element never attached would leave Base UI with nothing to focus, and
          focus outside a modal is worse than focus on the wrong control inside it.
          Returning true falls back to the default behaviour. */}
      <DialogContent initialFocus={() => inputRef.current ?? cancelRef.current ?? true}>
        <DialogHeader>
          <DialogTitle>{shown.title}</DialogTitle>
          <DialogDescription>{shown.description}</DialogDescription>
        </DialogHeader>

        {consequences && consequences.length > 0 && (
          <div className="space-y-1.5 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
            {/* Headed, because the frame alone had to carry the meaning and could not: the
                same box held what was being destroyed and what was not. */}
            <p className="font-medium text-destructive">This removes</p>
            <ul className="space-y-1">
              {consequences.map((line) => (
                <li key={line} className="text-muted-foreground">
                  {line}
                </li>
              ))}
            </ul>
          </div>
        )}

        {reassurances && reassurances.length > 0 && (
          <div className="space-y-1 text-sm">
            <p className="font-medium">Not affected</p>
            <ul className="space-y-1">
              {reassurances.map((line) => (
                <li key={line} className="text-muted-foreground">
                  {line}
                </li>
              ))}
            </ul>
          </div>
        )}

        {details}

        {shown.confirmText && (
          <Field>
            <FieldLabel htmlFor="confirm-text">
              Type{" "}
              {/* Monospaced on a tinted ground, the way slugs and package names are shown
                  elsewhere. A name like "Load Test 2" contains spaces, and in running text
                  there is no telling where it starts or whether they are part of it. */}
              <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[13px] font-medium text-foreground">
                {shown.confirmText}
              </code>{" "}
              to confirm
            </FieldLabel>
            <Input
              ref={inputRef}
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
          <DialogClose render={<Button ref={cancelRef} variant="outline">Cancel</Button>} />
          <Button
            variant="destructive"
            onClick={onConfirm}
            disabled={!confirmed || isPending}
          >
            {confirmIcon}
            {isPending ? `${confirmLabel}…` : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
