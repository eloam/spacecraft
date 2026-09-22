import type { ReactNode } from "react";
import { Orbit } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";

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
  return (
    // The page's main landmark: the shell provides one for every signed-in page, and
    // these screens render outside it.
    <main className="flex min-h-screen items-center justify-center bg-muted/40 px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-2 text-center">
          <Orbit className="size-7" aria-hidden />
          <h1 className="font-heading text-2xl font-medium tracking-tight">{title}</h1>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>

        <Card>
          <CardContent>{children}</CardContent>
        </Card>

        <p className="mt-6 text-center text-sm text-muted-foreground">{footer}</p>
      </div>
    </main>
  );
}
