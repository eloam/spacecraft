import type { ReactNode } from "react";
import { Orbit } from "lucide-react";
import { useTheme } from "@/hooks/useTheme";

/** Shared frame for the sign-in and sign-up screens. */
export function AuthCard({
  title,
  description,
  children,
  footer,
}: {
  title: string;
  description: string;
  children: ReactNode;
  footer: ReactNode;
}) {
  // The sign-in screens render outside AppShell, so the theme is applied here too.
  useTheme();

  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/40 px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-2 text-center">
          <Orbit className="size-7" aria-hidden />
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>

        <div className="rounded-xl border bg-card p-6 shadow-sm">{children}</div>

        <p className="mt-6 text-center text-sm text-muted-foreground">{footer}</p>
      </div>
    </div>
  );
}
