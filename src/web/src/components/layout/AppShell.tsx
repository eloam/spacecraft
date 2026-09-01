import { Link, Outlet, useParams } from "react-router-dom";
import { LogOut, Orbit, Settings } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth";
import { useLogout, useOrganizations } from "@/api/queries";
import { useCanonicalRoute, useOrganizationId } from "@/hooks/useRouteIdentity";
import { Spinner } from "@/components/ui/spinner";
import { ThemeToggle } from "@/components/ThemeToggle";
import { NotificationBell } from "@/components/layout/NotificationBell";
import { AccountAvatar } from "@/components/AccountAvatar";
import { OrganizationNav } from "@/components/layout/OrganizationNav";
import { OrganizationSwitcher } from "@/components/layout/OrganizationSwitcher";
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

  // Here rather than on each page: every route that names an organization or a project passes
  // through this frame, and an address reached by id should not stay spelled that way.
  useCanonicalRoute();

  // And nothing inside is rendered until the slug in the address has an id behind it.
  //
  // The pages ask the API by id, and the id comes from a list that takes a moment to arrive.
  // Their queries wait for it on their own, but their buttons did not: pressing one early
  // sent a request to /organizations//members, which the API answered 404 and the interface
  // reported as nothing at all. Rare by hand and reliable under load, which is where the
  // end-to-end tests found it.
  const organizations = useOrganizations();
  const organizationId = useOrganizationId(orgId ?? "");
  const resolvingOrganization =
    Boolean(orgId) && !organizationId && !organizations.isSuccess && !organizations.isError;

  return (
    <div className="min-h-screen">
      {/* Bordered only when nothing follows it. When the organization's sections are below,
          the two bars share a surface and a border between them would cut in half something
          meant to read as one. The section bar carries the closing border instead. */}
      <header
        className={cn(
          "sticky top-0 z-30 bg-topbar",
          !orgId && "border-b",
        )}
      >
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-2 px-4">
          <Link to="/" className="flex shrink-0 items-center gap-2 font-semibold tracking-tight">
            <Orbit className="size-5" aria-hidden />
            {/* The wordmark is the first thing to go on a narrow screen: keeping it pushed
                the account button off-screen entirely — 424px of content in a 375px viewport
                — leaving no way to sign out on a phone. sr-only rather than hidden, so the
                link keeps a name for assistive tech at every width. */}
            <span className="sr-only sm:not-sr-only">SpaceCraft</span>
          </Link>

          {/* On the same line as the product name, so which organization you are in is in
              the same place on every page rather than something to hunt for. Set apart from
              the wordmark by more than the bar's own gap: they are two different things —
              the product, and where you are inside it — and at a glance they should not
              read as one title. */}
          {orgId && (
            <div className="ml-3 flex min-w-0 items-center">
              <OrganizationSwitcher />
            </div>
          )}

          <div className="ml-auto flex shrink-0 items-center gap-1 sm:gap-2">
            <NotificationBell />
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

      {/* Organization sections live under the organization's name rather than in the bar
          above, so which organization they act on is never in question. */}
      {orgId && <OrganizationNav />}

      <main className="mx-auto max-w-6xl px-4 py-8">
        {resolvingOrganization ? (
          <div className="flex justify-center py-16">
            <Spinner className="size-5" />
          </div>
        ) : (
          <Outlet />
        )}
      </main>
    </div>
  );
}
