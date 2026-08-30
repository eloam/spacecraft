import { CheckCircle2, CircleDashed, MinusCircle, XCircle } from "lucide-react";
import { Spinner } from "@/components/ui/spinner";
import { cn } from "@/lib/utils";
import { RunStatus, StepStatus, runStatusLabel, stepStatusLabel } from "@/api/types";

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
        <Pill tone="running" icon={<Spinner className={ICON_CLASS} />} label="Running" />
      );
    case RunStatus.Cancelled:
      return <Pill tone="neutral" icon={<MinusCircle className={ICON_CLASS} />} label="Cancelled" />;
    default:
      return (
        <Pill tone="neutral" icon={<CircleDashed className={ICON_CLASS} />} label={runStatusLabel[status] ?? "Unknown"} />
      );
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
