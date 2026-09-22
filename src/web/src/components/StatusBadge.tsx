import { CheckCircle2, CircleDashed, Loader2Icon, MinusCircle, XCircle } from "lucide-react";
import { Spinner } from "@/components/ui/spinner";
import { cn } from "@/lib/utils";
import {
  PublicationStatus,
  RunStatus,
  StepStatus,
  runStatusLabel,
  stepStatusLabel,
} from "@/api/types";

const TONE = {
  neutral: "bg-muted text-muted-foreground",
  running: "bg-blue-500/10 text-blue-600 dark:text-blue-400",
  success: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
  danger: "bg-destructive/10 text-destructive",
} as const;

type Tone = keyof typeof TONE;

function Pill({ tone, icon, label }: { tone: Tone; icon: React.ReactNode; label: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium",
        TONE[tone],
      )}
    >
      {icon}
      {label}
    </span>
  );
}

const ICON_CLASS = "size-3.5";

export function RunStatusBadge({ status }: { status: number }) {
  switch (status) {
    case RunStatus.Succeeded:
      return <Pill tone="success" icon={<CheckCircle2 className={ICON_CLASS} />} label="Succeeded" />;
    case RunStatus.Failed:
      return <Pill tone="danger" icon={<XCircle className={ICON_CLASS} />} label="Failed" />;
    case RunStatus.Running:
      return (
        // The pill owns the colour and the spinner inherits it, so the tone survives the swap.
        // The raw icon rather than the shadcn Spinner: that one is a role="status" region of
        // its own, which read "Loading Running" and announced once per table row.
        <Pill
          tone="running"
          icon={<Loader2Icon className={cn(ICON_CLASS, "animate-spin")} aria-hidden />}
          label="Running"
        />
      );
    case RunStatus.Cancelled:
      return <Pill tone="neutral" icon={<MinusCircle className={ICON_CLASS} />} label="Cancelled" />;
    default:
      return (
        <Pill tone="neutral" icon={<CircleDashed className={ICON_CLASS} />} label={runStatusLabel[status] ?? "Unknown"} />
      );
  }
}

/**
 * A run's status as its icon alone, sized by the caller.
 *
 * For places where the state belongs beside a title rather than in a row of controls: a pill
 * there competes with the heading it qualifies, and the word it carries is already the first
 * thing the steps below spell out. The colour and the shape survive; only the label moves to
 * the accessible name.
 */
export function RunStatusIcon({ status, className }: { status: number; className?: string }) {
  const label = runStatusLabel[status] ?? "Unknown";
  const shared = cn("shrink-0", className);

  switch (status) {
    case RunStatus.Succeeded:
      return <CheckCircle2 className={cn(shared, "text-emerald-600 dark:text-emerald-400")} aria-label={label} />;
    case RunStatus.Failed:
      return <XCircle className={cn(shared, "text-destructive")} aria-label={label} />;
    case RunStatus.Running:
      return <Spinner className={cn(shared, "text-blue-600 dark:text-blue-400")} aria-label={label} />;
    case RunStatus.Cancelled:
      return <MinusCircle className={cn(shared, "text-muted-foreground")} aria-label={label} />;
    default:
      return <CircleDashed className={cn(shared, "text-muted-foreground")} aria-label={label} />;
  }
}

export function StepStatusIcon({ status }: { status: number }) {
  const label = stepStatusLabel[status] ?? "Unknown";

  switch (status) {
    case StepStatus.Succeeded:
      return <CheckCircle2 className="size-4 text-emerald-600 dark:text-emerald-400" aria-label={label} />;
    case StepStatus.Failed:
      return <XCircle className="size-4 text-destructive" aria-label={label} />;
    case StepStatus.Running:
      return <Spinner className="size-4 text-blue-600 dark:text-blue-400" aria-label={label} />;
    case StepStatus.Skipped:
      return <MinusCircle className="size-4 text-muted-foreground/60" aria-label={label} />;
    default:
      return <CircleDashed className="size-4 text-muted-foreground/60" aria-label={label} />;
  }
}

/**
 * The same pill runs use. Publications had their own badge with no icon, which rendered a
 * successful publish as a grey "Published" — the one status on that page worth seeing at a
 * glance, and the only one with no colour.
 */
export function PublicationStatusBadge({ status }: { status: number }) {
  switch (status) {
    case PublicationStatus.Succeeded:
      return <Pill tone="success" icon={<CheckCircle2 className={ICON_CLASS} />} label="Published" />;
    case PublicationStatus.Failed:
      return <Pill tone="danger" icon={<XCircle className={ICON_CLASS} />} label="Failed" />;
    case PublicationStatus.AlreadyExists:
      return <Pill tone="neutral" icon={<CircleDashed className={ICON_CLASS} />} label="Already there" />;
    default:
      // Pending, and anything a newer API adds.
      return <Pill tone="neutral" icon={<CircleDashed className={ICON_CLASS} />} label="Pending" />;
  }
}
