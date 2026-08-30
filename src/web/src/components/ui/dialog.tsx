"use client"

import * as React from "react"
import { Dialog as DialogPrimitive } from "@base-ui/react/dialog"

import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import { XIcon } from "lucide-react"

function Dialog({ ...props }: DialogPrimitive.Root.Props) {
  return <DialogPrimitive.Root data-slot="dialog" {...props} />
}

function DialogTrigger({ ...props }: DialogPrimitive.Trigger.Props) {
  return <DialogPrimitive.Trigger data-slot="dialog-trigger" {...props} />
}

function DialogPortal({ ...props }: DialogPrimitive.Portal.Props) {
  return <DialogPrimitive.Portal data-slot="dialog-portal" {...props} />
}

function DialogClose({ ...props }: DialogPrimitive.Close.Props) {
  return <DialogPrimitive.Close data-slot="dialog-close" {...props} />
}

function DialogOverlay({
  className,
  ...props
}: DialogPrimitive.Backdrop.Props) {
  return (
    <DialogPrimitive.Backdrop
      data-slot="dialog-overlay"
      className={cn(
        "fixed inset-0 isolate z-50 bg-black/35 duration-150 ease-out supports-backdrop-filter:backdrop-blur-sm data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0",
        className
      )}
      {...props}
    />
  )
}

function DialogContent({
  className,
  children,
  showCloseButton = true,
  ...props
}: DialogPrimitive.Popup.Props & {
  showCloseButton?: boolean
}) {
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Popup
        data-slot="dialog-content"
        className={cn(
          // grid-cols-[minmax(0,1fr)]: a grid item does not shrink below its content by
          // default, so one long unbreakable string — a URL — stretched the dialog
          // past its own max-width. This lets children truncate instead.
          //
          // max-h + overflow-y: the same problem vertically, and worse. A dialog is
          // centred with -translate-y-1/2, so content taller than the viewport spilled
          // off the top *and* the bottom — measured at 502px inside 420px, with Cancel
          // and Delete both unreachable and nothing to scroll. dvh, not vh, so a mobile
          // browser's collapsing toolbar does not hide the footer.
          "fixed top-1/2 left-1/2 z-50 grid max-h-[calc(100dvh-2rem)] grid-cols-[minmax(0,1fr)] overflow-y-auto w-full max-w-[calc(100%-2rem)] -translate-x-1/2 -translate-y-1/2 gap-6 rounded-xl bg-popover p-6 text-sm text-popover-foreground shadow-2xl ring-1 ring-foreground/10 duration-200 ease-out outline-none sm:max-w-md data-open:animate-in data-open:fade-in-0 data-open:zoom-in-98 data-closed:duration-150 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-98",
          className
        )}
        {...props}
      >
        {children}
        {showCloseButton && (
          <DialogPrimitive.Close
            data-slot="dialog-close"
            render={
              <Button
                variant="ghost"
                className="absolute top-4 right-4"
                size="icon-sm"
              />
            }
          >
            <XIcon
            />
            <span className="sr-only">Close</span>
          </DialogPrimitive.Close>
        )}
      </DialogPrimitive.Popup>
    </DialogPortal>
  )
}

/**
 * Sticky, because the popup itself scrolls. Measured before this: a dialog 461px tall in a
 * 398px viewport put its Delete button below the fold, and scrolling to reach it pushed
 * "Delete this credential?" out of view — the question left the screen while the answer was
 * being given.
 *
 * The negative margins pull the band over the popup's own padding so scrolling content
 * passes behind it instead of showing through the gap above.
 */
function DialogHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-header"
      className={cn(
        "sticky top-0 z-10 -mx-6 -mt-6 flex flex-col gap-1.5 bg-popover px-6 pt-6 pb-1",
        className
      )}
      {...props}
    />
  )
}

function DialogFooter({
  className,
  showCloseButton = false,
  children,
  ...props
}: React.ComponentProps<"div"> & {
  showCloseButton?: boolean
}) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn(
        "sticky bottom-0 z-10 -mx-6 -mb-6 flex flex-col-reverse gap-2 bg-popover px-6 pt-1 pb-6 sm:flex-row sm:justify-end",
        className
      )}
      {...props}
    >
      {children}
      {showCloseButton && (
        <DialogPrimitive.Close render={<Button variant="outline" />}>
          Close
        </DialogPrimitive.Close>
      )}
    </div>
  )
}

function DialogTitle({ className, ...props }: DialogPrimitive.Title.Props) {
  return (
    <DialogPrimitive.Title
      data-slot="dialog-title"
      // No size of its own, so it inherited the popup's text-sm and rendered at 14px —
      // the same size as its own description, and smaller than any card title in the
      // app. text-lg matches EmptyTitle, which is the other panel that opens this way.
      // leading-none went with it: at that height a title that wraps collides with
      // itself, and "Delete this credential?" wraps in a narrow dialog.
      className={cn("font-heading text-lg leading-snug font-medium tracking-tight", className)}
      {...props}
    />
  )
}

function DialogDescription({
  className,
  ...props
}: DialogPrimitive.Description.Props) {
  return (
    <DialogPrimitive.Description
      data-slot="dialog-description"
      className={cn(
        "text-sm text-muted-foreground *:[a]:underline *:[a]:underline-offset-3 *:[a]:hover:text-foreground",
        className
      )}
      {...props}
    />
  )
}

export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogOverlay,
  DialogPortal,
  DialogTitle,
  DialogTrigger,
}
