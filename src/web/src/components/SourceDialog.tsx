import type { ComponentProps } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DialogContent } from "@/components/ui/dialog";
import { Kbd, KbdGroup } from "@/components/ui/kbd";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

/**
 * The shell of a dialog that shows a document: as much of the screen as a dialog can take,
 * no padding so the editor runs edge to edge, and no stock close button — each of these
 * dialogs carries its own toolbar, and the way out belongs at the end of it.
 */
export function SourceDialogContent({
  className,
  ...props
}: Omit<ComponentProps<typeof DialogContent>, "showCloseButton">) {
  return (
    <DialogContent
      className={cn(
        "flex h-[92vh] w-[min(1600px,96vw)] max-w-none flex-col gap-0 overflow-hidden p-0 sm:max-w-none",
        className,
      )}
      showCloseButton={false}
      {...props}
    />
  );
}

/**
 * Previous and Next, for stepping through what a source dialog marks: the changes of a
 * diff, the findings of a validation. `noun` names them, for the tooltip and for a screen
 * reader alike.
 *
 * The shortcut hint rides on the app's tooltip, which opens on focus as well as hover; a
 * native title= never reaches a keyboard. `disabled` sits on the rendered Button, which is
 * where Base UI says it goes.
 */
export function StepButtons({
  noun,
  disabled,
  onStep,
}: {
  noun: "change" | "finding";
  disabled: boolean;
  onStep: (by: 1 | -1) => void;
}) {
  return (
    <>
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label={`Previous ${noun}`}
              disabled={disabled}
              onClick={() => onStep(-1)}
            />
          }
        >
          <ChevronUp className="size-4" aria-hidden />
        </TooltipTrigger>
        <TooltipContent>
          Previous {noun}
          <KbdGroup>
            <Kbd>Shift</Kbd>
            <span>+</span>
            <Kbd>F8</Kbd>
          </KbdGroup>
        </TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              size="icon-sm"
              variant="ghost"
              aria-label={`Next ${noun}`}
              disabled={disabled}
              onClick={() => onStep(1)}
            />
          }
        >
          <ChevronDown className="size-4" aria-hidden />
        </TooltipTrigger>
        <TooltipContent>
          Next {noun}
          <Kbd>F8</Kbd>
        </TooltipContent>
      </Tooltip>
    </>
  );
}
