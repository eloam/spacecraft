import { AlertCircle } from "lucide-react";
import { getErrorMessage, type FallbackProps } from "react-error-boundary";
import { Alert, AlertAction, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

/**
 * What an error boundary shows in place of a subtree that threw while rendering.
 *
 * Without one, React unmounts the whole tree and the user is looking at a blank page with
 * nothing to press. The message is the error's own — a render error is a bug, and the words
 * that name it are worth more to whoever reports it than a reassuring phrase — and the button
 * retries the render, which is what the boundary offers; a chunk that failed to load is
 * handled before it gets here, by reloading the page (see main.tsx).
 */
export function ErrorFallback({ error, resetErrorBoundary }: FallbackProps) {
  return (
    <Alert variant="destructive" role="alert">
      <AlertCircle className="size-4" aria-hidden />
      <AlertTitle>Something went wrong</AlertTitle>
      <AlertDescription>
        {getErrorMessage(error) ?? "This part of the page could not be shown."}
      </AlertDescription>
      <AlertAction>
        <Button variant="outline" size="sm" onClick={() => resetErrorBoundary()}>
          Try again
        </Button>
      </AlertAction>
    </Alert>
  );
}
