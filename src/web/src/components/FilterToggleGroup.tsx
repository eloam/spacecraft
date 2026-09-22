import type { LucideIcon } from "lucide-react";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { cn } from "@/lib/utils";

export interface FilterToggleItem<Value extends string> {
  value: Value;
  label: string;
  Icon: LucideIcon;
  /** The icon's colour while the item is pressed, as a Tailwind text class. */
  tone: string;
  count: number;
  disabled?: boolean;
}

/**
 * A segmented strip of filters, each with the count of what it would show, for the toolbar
 * over a table or a list.
 *
 * One control rather than loose buttons: a bordered set says the choices belong together
 * before anything is clicked. Exclusive by default — pressing one releases the rest, and
 * pressing the pressed one again empties `value`, which the caller reads as "back to all" —
 * or independent with `multiple`, where each item is on or off on its own. Base UI's
 * ToggleGroup underneath: every item is a real `aria-pressed` button, and the arrow keys walk
 * the set.
 *
 * The icons are the ones the column each filter selects on already uses, so a filter and
 * what it selects look alike — and, once pressed, the same colour too. Unpressed they stay
 * monochrome: several coloured icons competing at once is what the segmented control was
 * meant to quieten.
 *
 * font-medium on every item, not just the pressed one: a weight that changes on click is a
 * width that changes on click, and each press nudged the items beside it. Background and
 * colour say which is active without moving anything.
 */
export function FilterToggleGroup<Value extends string>({
  items,
  value,
  onValueChange,
  multiple = false,
  className,
  ...props
}: {
  items: readonly FilterToggleItem<Value>[];
  value: readonly Value[];
  onValueChange: (next: Value[]) => void;
  multiple?: boolean;
  "aria-label": string;
  className?: string;
}) {
  return (
    <ToggleGroup
      value={value}
      // Base UI's ToggleGroup is generic, but shadcn's wrapper fixes its value to string. It
      // only ever hands back values the items were given, so the widening is undone here
      // rather than at every call site.
      onValueChange={(next) => onValueChange(next as Value[])}
      multiple={multiple}
      // gap-0 rather than spacing={0}: spacing 0 is shadcn's joined look, which squares every
      // item so their borders meet. These pills keep their own corners inside the frame and
      // simply sit flush.
      className={cn("gap-0 rounded-lg border bg-card p-0.5", className)}
      {...props}
    >
      {items.map(({ value: itemValue, label, Icon, tone, count, disabled }) => {
        const pressed = value.includes(itemValue);

        return (
          <ToggleGroupItem
            key={itemValue}
            value={itemValue}
            disabled={disabled}
            // The pressed fill is toggleVariants' own aria-pressed:bg-muted; only the text
            // needs a pressed tone here, since it starts muted. hover:bg-transparent because
            // that same fill on hover made an unpressed pill look pressed while the pointer
            // crossed it: hovering changes the text, pressing changes the background.
            className="h-6 gap-1.5 rounded-[calc(var(--radius)-2px)] text-xs text-muted-foreground hover:bg-transparent data-pressed:text-foreground"
          >
            {/* From the controlled value rather than a data-pressed: variant, because
                Tailwind compiles whole class names and the tone is a class handed in. */}
            <Icon className={cn("size-3.5", pressed && tone)} aria-hidden />
            {label}
            <span className="tabular-nums opacity-60">{count}</span>
          </ToggleGroupItem>
        );
      })}
    </ToggleGroup>
  );
}
