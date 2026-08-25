import type { ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { ApiError } from "@/api/client";

/**
 * Renders the three states every query has, so pages don't each reinvent them.
 */
export function QueryBoundary({
  isLoading,
  error,
  children,
  rows = 3,
}: {
  isLoading: boolean;
  error: unknown;
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

  if (error) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="size-4" aria-hidden />
        <AlertTitle>{error instanceof ApiError ? error.title : "Something went wrong"}</AlertTitle>
        <AlertDescription>
          {error instanceof ApiError ? error.detail : "The request failed. Try again in a moment."}
        </AlertDescription>
      </Alert>
    );
  }

  return <>{children}</>;
}
