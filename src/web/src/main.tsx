import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ErrorBoundary } from "react-error-boundary";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ErrorFallback } from "@/components/ErrorFallback";
import { ApiError } from "@/api/client";
import App from "./App";
import "./index.css";

// A lazy chunk that no longer exists — the page was open across a deployment, and the file
// it asks for has been renamed by its hash — is not a render error to recover from in place:
// the bundle on the server is the one to run. Vite raises this event for exactly that case.
window.addEventListener("vite:preloadError", (event) => {
  event.preventDefault();
  window.location.reload();
});

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // Retrying a 4xx just repeats the same rejection; only transient faults are worth
        // a second attempt.
        if (error instanceof ApiError && error.status < 500) return false;
        return failureCount < 2;
      },
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {/* The last line of defence: without a boundary, an error thrown while rendering unmounts
        the whole tree and leaves a blank page. The shell has a closer one around its outlet,
        so a page's failure keeps the navigation; this one catches what happens outside it. */}
    <ErrorBoundary FallbackComponent={ErrorFallback}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          {/* Tooltips carry what truncation removes — a full registry URL, a timestamp behind
              "3 days ago" — so they are read, not glanced at. A short delay keeps them from
              firing while the pointer crosses a table on its way somewhere else; the provider
              then groups them, so moving between neighbouring cells skips the wait. */}
          <TooltipProvider delay={350}>
            <App />
          </TooltipProvider>
          <Toaster richColors position="top-right" />
        </BrowserRouter>
      </QueryClientProvider>
    </ErrorBoundary>
  </StrictMode>,
);
