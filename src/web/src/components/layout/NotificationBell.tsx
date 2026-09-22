import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Bell, CheckCheck, Settings2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Field, FieldContent, FieldDescription, FieldLabel } from "@/components/ui/field";
import { Switch } from "@/components/ui/switch";
import { timeAgo } from "@/lib/format";
import { cn } from "@/lib/utils";
import { describeError } from "@/api/client";
import {
  useClearNotifications,
  useDeleteNotification,
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
  const { data, isError, refetch } = useNotifications();
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const remove = useDeleteNotification();
  const clear = useClearNotifications();
  const navigate = useNavigate();

  // The menu is controlled so that it can get out of the way of the dialog: a popup closing
  // itself while a modal opens inside it leaves focus nowhere.
  const [menuOpen, setMenuOpen] = useState(false);
  const [confirmingClear, setConfirmingClear] = useState(false);
  const [alsoUnread, setAlsoUnread] = useState(false);

  const unread = data?.unreadCount ?? 0;
  const items = data?.items ?? [];

  const open = (notification: NotificationItem) => {
    if (!notification.readAt) {
      markRead.mutate(notification.id, {
        onError: (error) =>
          toast.error(describeError(error, "Could not mark the notification as read.")),
      });
    }

    navigate(notification.link);
  };

  const askToClear = () => {
    setMenuOpen(false);

    // Nothing unread means nothing to lose: these have all been read, and a dialog that only
    // ever gets one answer is a click, not a safeguard.
    if (unread === 0) {
      clear.mutate(false, {
        onError: (error) => toast.error(describeError(error, "Could not clear the notifications.")),
      });
      return;
    }

    // Unchecked every time. A choice this one remembered would eventually be made by
    // accident, and the expensive direction is the one that removes something unread.
    setAlsoUnread(false);
    setConfirmingClear(true);
  };

  return (
    <>
    <DropdownMenu
      open={menuOpen}
      // Asking on the way open, on top of the poll. Whatever the interval, the moment
      // somebody looks is the moment the list has to be right.
      onOpenChange={(isOpen) => {
        setMenuOpen(isOpen);

        if (isOpen) {
          void refetch();
        }
      }}
    >
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

      <DropdownMenuContent align="end" className="w-[min(23rem,calc(100vw-2rem))] p-0">
        <div className="flex items-center justify-between gap-2 px-3 py-2">
          <span className="text-sm font-medium">Notifications</span>

          <div className="flex items-center gap-0.5">
            {unread > 0 && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 gap-1.5 px-2 text-xs text-muted-foreground"
                onClick={() =>
                  markAll.mutate(undefined, {
                    onError: (error) =>
                      toast.error(describeError(error, "Could not mark everything as read.")),
                  })
                }
                disabled={markAll.isPending}
              >
                <CheckCheck className="size-3.5" aria-hidden />
                Mark all as read
              </Button>
            )}
            {items.length > 0 && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 gap-1.5 px-2 text-xs text-muted-foreground"
                onClick={askToClear}
                disabled={clear.isPending}
              >
                <Trash2 className="size-3.5" aria-hidden />
                Clear all
              </Button>
            )}
          </div>
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
            Nothing yet.
          </p>
        ) : (
          <ul className="max-h-96 overflow-y-auto">
            {items.map((notification) => (
              // Two controls side by side rather than one inside the other: a button nested
              // in a button is invalid, and the row is the larger target of the two.
              <li
                key={notification.id}
                className="group/row flex items-start transition-colors hover:bg-accent"
              >
                <button
                  type="button"
                  onClick={() => open(notification)}
                  className="flex min-w-0 flex-1 gap-2.5 py-2.5 pl-3 text-left"
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

                {/* Revealed by the row it belongs to, but never only by a mouse: it comes back
                    for keyboard focus, and stays put on anything without hover — where the
                    alternative is a control a phone could not reach. It keeps its space either
                    way, so a row does not shift under the pointer as it appears. */}
                <button
                  type="button"
                  onClick={() =>
                    remove.mutate(notification.id, {
                      onError: (error) =>
                        toast.error(describeError(error, "Could not dismiss the notification.")),
                    })
                  }
                  aria-label={`Dismiss "${notification.title}"`}
                  className="mt-2.5 mr-1.5 rounded p-1 text-muted-foreground/60 opacity-0 transition-opacity group-hover/row:opacity-100 hover:bg-background hover:text-foreground focus-visible:opacity-100 [@media(hover:none)]:opacity-100"
                >
                  <X className="size-3.5" aria-hidden />
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

    <Dialog open={confirmingClear} onOpenChange={setConfirmingClear}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Clear notifications?</DialogTitle>
          <DialogDescription>
            Everything you have already read is removed. Anything still unread stays, unless
            you say otherwise.
          </DialogDescription>
        </DialogHeader>

        <Field orientation="horizontal" className="rounded-lg border p-3">
          <FieldContent>
            <FieldLabel htmlFor="clear-unread">Remove unread as well</FieldLabel>
            <FieldDescription>
              {unread === 1
                ? "One notification you have not read yet would go too."
                : `${unread} notifications you have not read yet would go too.`}
            </FieldDescription>
          </FieldContent>

          <Switch id="clear-unread" checked={alsoUnread} onCheckedChange={setAlsoUnread} />
        </Field>

        <DialogFooter>
          <DialogClose render={<Button type="button" variant="outline">Cancel</Button>} />
          <Button
            onClick={() =>
              clear.mutate(alsoUnread, {
                onSuccess: () => setConfirmingClear(false),
                onError: (error) =>
                  toast.error(describeError(error, "Could not clear the notifications.")),
              })
            }
            disabled={clear.isPending}
          >
            <Trash2 className="size-4" aria-hidden />
            {clear.isPending ? "Clearing…" : "Clear"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    </>
  );
}
