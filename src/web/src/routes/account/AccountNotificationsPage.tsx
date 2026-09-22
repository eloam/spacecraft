import { useState } from "react";
import { Bell, Hash, Mail } from "lucide-react";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { SettingsSection } from "@/components/SettingsSections";
import { QueryBoundary } from "@/components/QueryBoundary";
import { describeError } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import {
  useNotificationPreferences,
  useSaveNotificationPreferences,
  type NotificationChannel,
  type NotificationKind,
  type NotificationPreference,
} from "@/api/queries";

/**
 * What each event is, in the reader's terms rather than the enum's.
 *
 * Who receives what is part of the description on purpose: an owner reading "someone joins"
 * should not have to try it to find out whether a member would have been told too.
 */
const EVENTS: { kind: NotificationKind; label: string; description: string }[] = [
  {
    kind: "MemberJoined",
    label: "Someone joins",
    description: "An invitation was accepted. Sent to owners and admins.",
  },
  {
    kind: "MemberLeft",
    label: "Someone leaves",
    description: "A member left or was removed. Sent to owners and admins.",
  },
  {
    kind: "RunCompleted",
    label: "A run finishes",
    description:
      "The result of a run you started — the version published, or the step that failed and why.",
  },
  {
    kind: "InvitationDeclined",
    label: "An invitation is declined",
    description: "Somebody you invited turned it down. Sent to owners and admins.",
  },
  {
    kind: "RemovedFromOrganization",
    label: "You are removed from an organization",
    description:
      "Somebody took your access to an organization away. Sent to you, and only when it was not you who left.",
  },
];

function EventSwitch({
  label,
  description,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  description: string;
  checked: boolean;
  disabled?: boolean;
  onChange?: (value: boolean) => void;
}) {
  return (
    <label className="flex items-start justify-between gap-4 py-2.5">
      <span className="min-w-0">
        <span className="block text-sm font-medium">{label}</span>
        <span className="mt-0.5 block text-sm text-muted-foreground">{description}</span>
      </span>
      <Switch
        checked={checked}
        disabled={disabled}
        onCheckedChange={onChange}
        className="mt-0.5 shrink-0"
      />
    </label>
  );
}

/** A channel that exists but does not deliver yet. The switches are shown, and honest about it. */
function PlannedChannel({
  icon,
  title,
  description,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
}) {
  return (
    <SettingsSection icon={icon} title={title} description={description}>
      <div className="divide-y opacity-60">
        {EVENTS.map((event) => (
          <EventSwitch
            key={event.kind}
            label={event.label}
            description={event.description}
            checked={false}
            disabled
          />
        ))}
      </div>
    </SettingsSection>
  );
}

export function AccountNotificationsPage() {
  const email = useAuthStore((s) => s.user?.email);
  const query = useNotificationPreferences();
  const save = useSaveNotificationPreferences();

  // Held locally so a switch moves under the finger rather than after a round trip, and
  // derived during render rather than copied into state by an effect — the server is the
  // source of truth until somebody touches a switch, and the local answer afterwards. A
  // failed save puts the previous value back: a switch that stays where you left it while
  // the save failed is a lie.
  const [draft, setDraft] = useState<NotificationPreference[] | null>(null);
  const matrix = draft ?? query.data ?? [];

  const isOn = (kind: NotificationKind, channel: NotificationChannel) =>
    matrix.find((p) => p.kind === kind && p.channel === channel)?.enabled ?? false;

  const toggle = (kind: NotificationKind, channel: NotificationChannel, enabled: boolean) => {
    const previous = matrix;
    const next = previous.map((p) =>
      p.kind === kind && p.channel === channel ? { ...p, enabled } : p,
    );

    setDraft(next);

    save.mutate(next, {
      onError: (error) => {
        setDraft(previous);
        toast.error(describeError(error, "Could not save your notification settings."));
      },
    });
  };

  const channel = (
    kind: NotificationChannel,
    icon: React.ReactNode,
    title: string,
    description: string,
  ) => (
    <SettingsSection icon={icon} title={title} description={description}>
      <div className="divide-y">
        {EVENTS.map((event) => (
          <EventSwitch
            key={event.kind}
            label={event.label}
            description={event.description}
            checked={isOn(event.kind, kind)}
            onChange={(value) => toggle(event.kind, kind, value)}
          />
        ))}
      </div>
    </SettingsSection>
  );

  return (
    <div className="space-y-6">
      <QueryBoundary
        isLoading={query.isLoading}
        error={query.error}
        hasData={query.data !== undefined}
        rows={2}
      >
        {matrix.length === 0 ? (
          <Skeleton className="h-40 w-full" />
        ) : (
          <div className="space-y-6">
            {channel(
              "InApp",
              <Bell />,
              "In-app",
              "The bell in the top bar, with everything that has happened recently.",
            )}
            {channel(
              "Email",
              <Mail />,
              "Email",
              email ? `Sent to ${email}.` : "Sent to your account's address.",
            )}
          </div>
        )}
      </QueryBoundary>

      <PlannedChannel
        icon={<Hash />}
        title="Slack"
        description="Not connected yet. Once a workspace can be linked, these switches turn on."
      />
    </div>
  );
}
