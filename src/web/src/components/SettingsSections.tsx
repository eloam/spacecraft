import type { ReactNode } from "react";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/utils";

/**
 * The icon in its rounded surface, the way every empty state in the app introduces itself.
 * Laid out on the side rather than above, because a settings block reads left to right and
 * its form follows underneath — but it is the same 40px square, the same radius, and the
 * same muted ground, so the two are recognisably one idea.
 */
function SectionHeader({
  icon,
  title,
  description,
  destructive,
}: {
  icon?: ReactNode;
  title: string;
  description: ReactNode;
  destructive?: boolean;
}) {
  return (
    <CardHeader className="grid-cols-[auto_1fr] items-start gap-x-3.5">
      {icon && (
        <div
          className={cn(
            "row-span-2 flex size-10 shrink-0 items-center justify-center rounded-lg [&_svg:not([class*='size-'])]:size-5",
            destructive ? "bg-destructive/10 text-destructive" : "bg-muted text-foreground",
          )}
          // Decorative: the title beside it says what the section is.
          aria-hidden
        >
          {icon}
        </div>
      )}
      {/* A heading rather than CardTitle, which renders a div: each settings block is a
          section a screen reader should find in the page's outline. Its classes are
          CardTitle's so it looks the same. */}
      <h2
        data-slot="card-title"
        className={cn(
          "font-heading text-base leading-normal font-medium group-data-[size=sm]/card:text-sm",
          destructive && "text-destructive",
        )}
      >
        {title}
      </h2>
      <CardDescription>{description}</CardDescription>
    </CardHeader>
  );
}

/** A titled block of settings. */
export function SettingsSection({
  icon,
  title,
  description,
  children,
}: {
  icon?: ReactNode;
  title: string;
  description: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card>
      <SectionHeader icon={icon} title={title} description={description} />
      <CardContent>{children}</CardContent>
    </Card>
  );
}

/**
 * The same block, marked as irreversible.
 *
 * Kept visually distinct and placed last: a destructive action sitting beside ordinary
 * settings gets clicked by muscle memory. The icon carries the warning too — a red frame
 * around a grey chip reads as a section that happens to be outlined, not as a danger.
 */
export function DangerSection({
  icon,
  title,
  description,
  children,
}: {
  icon?: ReactNode;
  title: string;
  description: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card className="border border-destructive/40">
      <SectionHeader icon={icon} title={title} description={description} destructive />
      <CardContent>{children}</CardContent>
    </Card>
  );
}
