import type { ComponentType, ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * The small framed panel that states the terms of something: what a deletion removes, what a
 * run will do, what a credential publishes for, what stands between you and closing your
 * account.
 *
 * It was the same nine classes copied into four files, which is how the four had already begun
 * to drift — one of them a step tighter than the others. The surface is deliberately muted
 * rather than coloured: these panels carry facts, and a red frame around facts spends the
 * warning before the reader reaches the button it belongs on.
 */
export function Panel({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <div className={cn("space-y-3 rounded-2xl border bg-muted/40 p-3.5 text-sm", className)}>
      {children}
    </div>
  );
}

/**
 * The eyebrow above a group — small, spaced, upper case. It carries the only colour in the
 * panel when what follows is a loss, and none when it is not.
 */
export function PanelLabel({
  tone = "muted",
  children,
}: {
  /** Destructive on what is being taken away, muted on everything else. */
  tone?: "muted" | "destructive";
  children: ReactNode;
}) {
  return (
    <p
      className={cn(
        "text-xs font-medium tracking-wider uppercase",
        tone === "destructive" ? "text-destructive" : "text-muted-foreground",
      )}
    >
      {children}
    </p>
  );
}

/** A labelled list inside a panel. */
export function PanelSection({
  label,
  tone = "muted",
  children,
}: {
  label: string;
  tone?: "muted" | "destructive";
  children: ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <PanelLabel tone={tone}>{label}</PanelLabel>
      <ul className="space-y-1.5">{children}</ul>
    </div>
  );
}

/**
 * One line of a section: an icon and a sentence.
 *
 * The icon carries the direction, so a line does not have to open with "no longer" or "loses"
 * to be read as a loss.
 */
export function PanelLine({
  icon: Icon,
  tone = "muted",
  muted = false,
  children,
}: {
  icon: ComponentType<{ className?: string; "aria-hidden"?: boolean }>;
  tone?: "muted" | "destructive";
  /** Whether the text is muted too, or only its icon. */
  muted?: boolean;
  children: ReactNode;
}) {
  return (
    <li className="flex items-start gap-2.5">
      <Icon
        className={cn(
          "mt-0.5 size-3.5 shrink-0",
          tone === "destructive" ? "text-destructive" : "text-muted-foreground",
        )}
        aria-hidden
      />
      <span className={cn(muted && "text-muted-foreground")}>{children}</span>
    </li>
  );
}
