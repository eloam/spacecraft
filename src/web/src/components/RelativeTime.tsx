import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { formatDateTime, timeAgo } from "@/lib/format";

/**
 * "3 days ago", with the exact instant behind it.
 *
 * A `<time>` element carrying the machine-readable value, and the exact moment in a tooltip
 * on a focusable trigger: the tables used to hang the tooltip off a bare `<span>`, which a
 * pointer could reach and a keyboard could not, so the precise timestamp was hover-only.
 */
export function RelativeTime({ iso, className }: { iso: string; className?: string }) {
  return (
    <Tooltip>
      {/* Base UI's trigger is a <button> by default, so it is in the tab order as it is. */}
      <TooltipTrigger className={className}>
        <time dateTime={iso}>{timeAgo(iso)}</time>
      </TooltipTrigger>
      <TooltipContent>{formatDateTime(iso)}</TooltipContent>
    </Tooltip>
  );
}
