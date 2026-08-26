import { Link, Outlet, useParams } from "react-router-dom";
import { LogOut, Orbit, Settings } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { useAuthStore } from "@/stores/auth";
import { useLogout } from "@/api/queries";
import { ThemeToggle } from "@/components/ThemeToggle";
import { AccountAvatar } from "@/components/AccountAvatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

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
                  Credentials
                </Link>
              </>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-2">
            <ThemeToggle />

            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <Button variant="ghost" size="sm">
                    <AccountAvatar displayName={user?.displayName} email={user?.email} />
                    {/* The name is the label on wider screens; on a phone the icon alone
                        carries it, so the accessible name comes from aria-label. */}
                    <span className="hidden max-w-40 truncate sm:inline">
                      {user?.displayName || user?.email}
                    </span>
                  </Button>
                }
                aria-label="Account menu"
              />
              <DropdownMenuContent align="end" className="w-52">
                <DropdownMenuItem render={<Link to="/account" />}>
                  <Settings className="size-4" aria-hidden />
                  Settings
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  onClick={() => logout.mutate()}
                  disabled={logout.isPending}
                >
                  <LogOut className="size-4" aria-hidden />
                  Sign out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
