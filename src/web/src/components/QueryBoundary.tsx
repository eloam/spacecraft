import type { ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { ApiError, describeError } from "@/api/client";

/**
 * Renders the three states every query has, so pages don't each reinvent them.
 */
export function QueryBoundary({
  isLoading,
  error,
  hasData = false,
  children,
  rows = 3,
}: {
  isLoading: boolean;
  error: unknown;
  /**
   * Whether the query still holds data from an earlier success. React Query keeps it
   * through a failed refetch, and the run pages poll every two seconds: without this, one
   * dropped poll replaced a correct table with a full-width error, and the next poll put it
   * back. Pass `query.data !== undefined` wherever a query refetches or polls.
   */
  hasData?: boolean;
  children: ReactNode;
  rows?: number;
}) {
  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: rows }, (_, i) => (
          <Skeleton key={i} className="h-16 w-full" />
        ))}
      </div>
    );
  }

  // Stale data beats no data: keep showing it and say, quietly, that the last refresh
  // failed. The full-width alert is for the case where there is nothing behind it.
  if (error && hasData) {
    return (
      <div className="space-y-3">
        <p className="text-sm text-muted-foreground" role="status">
          {describeError(error, "Could not refresh.")} Showing the last version loaded.
        </p>
        {children}
      </div>
    );
  }

  if (error) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="size-4" aria-hidden />
        <AlertTitle>{error instanceof ApiError ? error.title : "Something went wrong"}</AlertTitle>
        <AlertDescription>
          {describeError(error, "The request failed. Try again in a moment.")}
        </AlertDescription>
      </Alert>
    );
  }

  return <>{children}</>;
}
