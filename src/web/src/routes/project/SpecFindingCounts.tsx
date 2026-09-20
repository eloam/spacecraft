import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
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
      {severities.map(({ count, Icon, tone, noun }) => {
        const label = `${count} ${noun}${count === 1 ? "" : "s"}`;

        return (
          // An icon and a number are an abbreviation of the sentence the tooltip spells out.
          <Tooltip key={noun}>
            <TooltipTrigger
              // Zeroes stay in place rather than collapsing: the three always sit in the same
              // order, so a column of rows can be read down instead of decoded row by row.
              className={cn("flex items-center gap-1", count > 0 ? tone : "opacity-30")}
            >
              <Icon className="size-3.5" aria-hidden />
              {/* The cell read aloud was "1 0 3" — three bare numbers naming nothing. The
                  sentence is in the markup too, so the column is legible without opening
                  anything. */}
              <span aria-hidden>{count}</span>
              <span className="sr-only">{label}</span>
            </TooltipTrigger>
            <TooltipContent>{label}</TooltipContent>
          </Tooltip>
        );
      })}
    </div>
  );
}
