import { Link, useNavigate } from "react-router-dom";
import { Bell, CheckCheck, Settings2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { timeAgo } from "@/lib/format";
import { cn } from "@/lib/utils";
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useNotifications,
  type NotificationItem,
} from "@/api/queries";

/**
 * What happened while you were not looking.
 *
 * Opening a notification marks it read and goes where it points, because those are the same
 * intention: nobody reads a run failure and then wants to be told about it again.
 */
export function NotificationBell() {
  const { data, isError } = useNotifications();
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const navigate = useNavigate();

  const unread = data?.unreadCount ?? 0;
  const items = data?.items ?? [];

  const open = (notification: NotificationItem) => {
    if (!notification.readAt) {
      markRead.mutate(notification.id);
    }

    navigate(notification.link);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="ghost" size="sm" className="relative">
            <Bell className="size-4" aria-hidden />
            {unread > 0 && (
              // On the icon rather than beside it: the count is a property of the bell, and
              // a number that reflows the toolbar as it changes is a number nobody trusts.
              <span
                className="absolute -top-0.5 right-0.5 min-w-4 rounded-full bg-primary px-1 text-[10px] font-medium leading-4 text-primary-foreground"
                aria-hidden
              >
                {unread > 9 ? "9+" : unread}
              </span>
            )}
          </Button>
        }
        aria-label={unread > 0 ? `Notifications, ${unread} unread` : "Notifications"}
      />

      <DropdownMenuContent align="end" className="w-[min(22rem,calc(100vw-2rem))] p-0">
        <div className="flex items-center justify-between gap-2 px-3 py-2">
          <span className="text-sm font-medium">Notifications</span>
          {unread > 0 && (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 gap-1.5 px-2 text-xs text-muted-foreground"
              onClick={() => markAll.mutate()}
              disabled={markAll.isPending}
            >
              <CheckCheck className="size-3.5" aria-hidden />
              Mark all as read
            </Button>
          )}
        </div>

        <DropdownMenuSeparator className="my-0" />

        {isError && !data ? (
          // Not the empty state. A request that failed and an account with nothing to read
          // look identical from here, and telling somebody they have no notifications when
          // the truth is that we could not ask is the worse of the two mistakes.
          <p className="px-3 py-6 text-center text-sm text-muted-foreground" role="status">
            Could not load your notifications. They will reappear on the next refresh.
          </p>
        ) : items.length === 0 ? (
          <p className="px-3 py-6 text-center text-sm text-muted-foreground">
            Nothing yet. Runs and membership changes will show up here.
          </p>
        ) : (
          <ul className="max-h-96 overflow-y-auto">
            {items.map((notification) => (
              <li key={notification.id}>
                <button
                  type="button"
                  onClick={() => open(notification)}
                  className="flex w-full gap-2.5 px-3 py-2.5 text-left transition-colors hover:bg-accent"
                >
                  {/* The dot is the only thing separating read from unread, so it holds a
                      column of its own rather than sitting inline where a long title would
                      push it out of alignment. */}
                  <span
                    className={cn(
                      "mt-1.5 size-1.5 shrink-0 rounded-full",
                      notification.readAt ? "bg-transparent" : "bg-primary",
                    )}
                    aria-hidden
                  />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium">{notification.title}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">
                      {notification.summary}
                    </span>
                    <span className="mt-1 block text-xs text-muted-foreground/80">
                      {notification.organizationName} · {timeAgo(notification.createdAt)}
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}

        <DropdownMenuSeparator className="my-0" />

        <Link
          to="/account/notifications"
          className="flex items-center gap-2 px-3 py-2 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <Settings2 className="size-3.5" aria-hidden />
          Notification settings
        </Link>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
