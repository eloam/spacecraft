import { Link, Outlet, useParams } from "react-router-dom";
import { KeyRound, LogOut, Orbit } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { useAuthStore } from "@/stores/auth";
import { useLogout } from "@/api/queries";
import { ThemeToggle } from "@/components/ThemeToggle";

export function AppShell() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { orgId } = useParams();

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b sticky top-0 z-30 bg-background/85 backdrop-blur">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-4 px-4">
          <Link to="/" className="flex items-center gap-2 font-semibold tracking-tight">
            <Orbit className="size-5" aria-hidden />
            SpaceCraft
          </Link>

          <nav className="ml-4 flex items-center gap-1 text-sm">
            {/* shadcn's Button is built on Base UI, which composes via a `render` prop
                rather than Radix's `asChild`. Applying the variants to the link directly
                is simpler here and keeps router semantics intact. */}
            {orgId && (
              <>
                <Link to={`/orgs/${orgId}`} className={buttonVariants({ variant: "ghost", size: "sm" })}>
                  Projects
                </Link>
                <Link
                  to={`/orgs/${orgId}/credentials`}
                  className={buttonVariants({ variant: "ghost", size: "sm" })}
                >
                  <KeyRound className="size-4" aria-hidden />
                  Credentials
                </Link>
              </>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            <span className="hidden text-sm text-muted-foreground sm:inline">
              {user?.displayName || user?.email}
            </span>
            <ThemeToggle />
            <Button
              variant="ghost"
              size="sm"
              onClick={() => logout.mutate()}
              disabled={logout.isPending}
            >
              <LogOut className="size-4" aria-hidden />
              <span className="sr-only sm:not-sr-only">Sign out</span>
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
