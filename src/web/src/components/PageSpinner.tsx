import { Spinner } from "@/components/ui/spinner";

/** Where a page will be, while it cannot be drawn yet: its chunk, or what it waits on, is loading. */
export function PageSpinner() {
  return (
    <div className="flex justify-center py-16">
      <Spinner className="size-5" />
    </div>
  );
}
