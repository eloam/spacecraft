import type { ReactNode } from "react";
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty";

/**
 * The registry's `Empty` behind the four props every caller here actually passes.
 *
 * Nine pages render an empty state and all of them say the same four things, so the
 * composition is written once. What it gains by sitting on the primitive rather than a
 * hand-rolled dashed box: the icon in a rounded surface, the serif title, and the
 * spacing the rest of the theme is measured against.
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon?: ReactNode;
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <Empty className="rounded-2xl border border-dashed bg-card">
      <EmptyHeader>
        {icon && <EmptyMedia variant="icon">{icon}</EmptyMedia>}
        <EmptyTitle>{title}</EmptyTitle>
        <EmptyDescription>{description}</EmptyDescription>
      </EmptyHeader>
      {action && <EmptyContent>{action}</EmptyContent>}
    </Empty>
  );
}
