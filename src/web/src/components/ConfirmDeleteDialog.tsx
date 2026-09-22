import { useRef, useState, type ReactNode, type RefObject } from "react";
import { Check, Minus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, FieldLabel } from "@/components/ui/field";
import { Separator } from "@/components/ui/separator";
import { Panel, PanelLine, PanelSection } from "@/components/Panel";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";

interface ConfirmDeleteProps {
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
  details?: ReactNode;
  isPending?: boolean;
  error?: string | null;
  onConfirm: () => void;
}

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
  onOpenChangeComplete,
  ...body
}: ConfirmDeleteProps & {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /**
   * Fired once the close animation has finished, which is when a caller may forget the row
   * it opened this for. Forgetting it in `onOpenChange` instead showed the title fading out
   * as "Delete spec #undefined?" — the dialog is still on screen for the length of its exit.
   */
  onOpenChangeComplete?: (open: boolean) => void;
}) {
  // Base UI focuses the first focusable child, which here is whatever `details` renders —
  // on the credentials dialog that is a link out of the page, so Enter on open navigated
  // away. Point it at the confirmation field, or at Cancel when there is none.
  const inputRef = useRef<HTMLInputElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  return (
    <Dialog open={open} onOpenChange={onOpenChange} onOpenChangeComplete={onOpenChangeComplete}>
      {/* Resolved when focus is placed, not when the element is described: a bare ref
          whose element never attached would leave Base UI with nothing to focus, and
          focus outside a modal is worse than focus on the wrong control inside it.
          Returning true falls back to the default behaviour. */}
      <DialogContent initialFocus={() => inputRef.current ?? cancelRef.current ?? true}>
        {/* The typed confirmation lives in a child of the content, which Base UI unmounts
            once the dialog has closed: every opening starts with an empty field, whether
            the last one was cancelled or closed by a delete that went through. Kept in this
            component instead, it once survived into the next row bearing the same name, and a
            permanent delete was one click away from a dialog that looked as careful as ever. */}
        <ConfirmDeleteBody inputRef={inputRef} cancelRef={cancelRef} {...body} />
      </DialogContent>
    </Dialog>
  );
}

function ConfirmDeleteBody({
  inputRef,
  cancelRef,
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
}: ConfirmDeleteProps & {
  inputRef: RefObject<HTMLInputElement | null>;
  cancelRef: RefObject<HTMLButtonElement | null>;
}) {
  const [typed, setTyped] = useState("");
  const confirmed = !confirmText || typed.trim() === confirmText;

  return (
    <>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>

      {/* One panel, two ledgers. The red frame this replaces put a warning colour around
          facts, so by the time the reader reached the destructive button the colour had
          already been spent — and it framed information the dialog states calmly one line
          above. Red belongs on the action; these are the terms of it.

          Same muted surface as the "still publishing" panel below, which says the same
          kind of thing and had no reason to look like a different component. */}
      {((consequences && consequences.length > 0) ||
        (reassurances && reassurances.length > 0)) && (
        <Panel>
          {consequences && consequences.length > 0 && (
            <PanelSection label="This removes" tone="destructive">
              {consequences.map((line) => (
                <PanelLine key={line} icon={Minus} tone="destructive">
                  {line}
                </PanelLine>
              ))}
            </PanelSection>
          )}

          {consequences?.length && reassurances?.length ? <Separator /> : null}

          {reassurances && reassurances.length > 0 && (
            // Muted, not green: these are reassurances, not achievements, and the only
            // colour in this panel should be the one marking a loss.
            <PanelSection label="Not affected">
              {reassurances.map((line) => (
                <PanelLine key={line} icon={Check} muted>
                  {line}
                </PanelLine>
              ))}
            </PanelSection>
          )}
        </Panel>
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
    </>
  );
}
