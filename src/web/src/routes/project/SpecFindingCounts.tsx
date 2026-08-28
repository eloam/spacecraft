import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { SpecValidationStatus } from "@/api/types";

/**
 * All three severities at once, rather than the single badge for whichever is worst.
 *
 * A document with one error and three hundred notes said "1 error" and nothing else, so the
 * only way to learn there was anything more was to open it.
 */
export function SpecFindingCounts({
  status,
  information,
  warnings,
  errors,
}: {
  status: number;
  information: number;
  warnings: number;
  errors: number;
}) {
  if (status === SpecValidationStatus.Pending) {
    return <Badge variant="secondary">Pending</Badge>;
  }

  if (errors + warnings + information === 0) {
    return (
      <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <CircleCheck className="size-3.5" aria-hidden />
        Clean
      </span>
    );
  }

  const severities = [
    { count: errors, Icon: CircleAlert, tone: "text-destructive", noun: "error" },
    { count: warnings, Icon: TriangleAlert, tone: "text-[var(--code-warning)]", noun: "warning" },
    { count: information, Icon: Info, tone: "text-muted-foreground", noun: "note" },
  ];

  return (
    <div className="flex items-center gap-2.5 text-xs tabular-nums">
      {severities.map(({ count, Icon, tone, noun }) => (
        <span
          key={noun}
          // Zeroes stay in place rather than collapsing: the three always sit in the same
          // order, so a column of rows can be read down instead of decoded row by row.
          className={cn("flex items-center gap-1", count > 0 ? tone : "opacity-30")}
          title={`${count} ${noun}${count === 1 ? "" : "s"}`}
        >
          <Icon className="size-3.5" aria-hidden />
          {count}
        </span>
      ))}
    </div>
  );
}
