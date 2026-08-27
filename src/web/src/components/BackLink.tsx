import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * The way back out of a page, sitting just above its title.
 *
 * It replaces a breadcrumb trail on pages that are one step deep: the trail spent a line
 * restating an organization the top bar already names, to offer a single link worth taking.
 *
 * The destination is written out rather than left to a bare arrow, which says "back" without
 * saying back to what — and because the text is visible, it is also the accessible name, so
 * no aria-label has to repeat it.
 */
export function BackLink({
  to,
  children,
  className,
}: {
  to: string;
  /** Name the list being returned to, not the action: "All projects", not "Go back". */
  children: ReactNode;
  className?: string;
}) {
  return (
    <Link
      to={to}
      className={cn(
        "inline-flex items-center gap-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground",
        className,
      )}
    >
      <ArrowLeft className="size-4" aria-hidden />
      {children}
    </Link>
  );
}
