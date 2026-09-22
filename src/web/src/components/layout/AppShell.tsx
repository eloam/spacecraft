import { Suspense } from "react";
import { Link, Outlet, useLocation, useParams } from "react-router-dom";
import { ErrorBoundary } from "react-error-boundary";
import { LogOut, Orbit, SearchX, Settings } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { useAccount, useLogout, useOrganizations } from "@/api/queries";
import { useCanonicalRoute } from "@/hooks/useRouteIdentity";
import { PageSpinner } from "@/components/PageSpinner";
import { EmptyState } from "@/components/EmptyState";
import { ErrorFallback } from "@/components/ErrorFallback";
import { QueryBoundary } from "@/components/QueryBoundary";
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
  // Who is signed in is server state: the account query, as the account pages read it — not
  // a copy in the session store.
  const account = useAccount();
  const logout = useLogout();
  const { orgSlug } = useParams();
  const location = useLocation();

  // Here rather than on each page: every route that names an organization or a project passes
  // through this frame, and an address reached by id should not stay spelled that way.
  useCanonicalRoute();

  // And nothing inside is rendered until the list of your organizations has arrived and been
  // looked through for the one in the address.
  //
  // The pages ask the API by id, and a slug becomes an id through that list, which takes a
  // moment to arrive. Their queries wait for it on their own, but their buttons did not:
  // pressing one early sent a request to /organizations//members, which the API answered 404
  // and the interface reported as nothing at all. Rare by hand and reliable under load, which
  // is where the end-to-end tests found it.
  //
  // An address that carries the id needs no lookup, and used to skip the wait — and with it
  // the check below: the id of an organization you are not in rendered this frame with the
  // guid for a name and the section nav around each page's own refusal. So both forms wait
  // for the list, and both are looked for in it.
  const organizations = useOrganizations();
  const resolvingOrganization = Boolean(orgSlug) && organizations.isPending;
  const known = organizations.data?.some((o) => o.slug === orgSlug || o.id === orgSlug) ?? false;

  // The list arrived and this address is not in it. Before the pages asked by id, the slug
  // went straight to the API, which answered 404 and the page said so; now their queries are
  // simply never enabled, and what a stranger following a link got was a spinner that would
  // have turned for ever.
  //
  // Only once the list did arrive: a list that failed to load says nothing about who is a
  // member of what, and used to be reported in these words too — "not found" over a network
  // fault. That case shows the failure itself, below.
  //
  // Worded like the API's own refusal, which does not distinguish "no such organization" from
  // "not yours" — telling those apart would confirm which ids are real to anyone who asks.
  const unknownOrganization = Boolean(orgSlug) && organizations.isSuccess && !known;

  return (
    <div className="min-h-screen">
      {/* Bordered only when nothing follows it. When the organization's sections are below,
          the two bars share a surface and a border between them would cut in half something
          meant to read as one. The section bar carries the closing border instead. */}
      <header className={cn("sticky top-0 z-30 bg-topbar", !orgSlug && "border-b")}>
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
          {orgSlug && (
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
                    <AccountAvatar
                      displayName={account.data?.displayName}
                      email={account.data?.email}
                    />
                    {/* The name is the label on wider screens; on a phone the icon alone
                        carries it, so the accessible name comes from aria-label. */}
                    <span className="hidden max-w-40 truncate sm:inline">
                      {account.data?.displayName || account.data?.email}
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
                <DropdownMenuItem onClick={() => logout.mutate()} disabled={logout.isPending}>
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
      {orgSlug && !unknownOrganization && <OrganizationNav />}

      <main className="mx-auto max-w-6xl px-4 py-8">
        {resolvingOrganization ? (
          <PageSpinner />
        ) : (
          /* The list never loaded: the alert every page shows for a failed query, in the
             page's place, since both forms of the address wait for that list before a page
             is rendered at all. A list that loaded once and then failed to refresh is not
             that: the pages still have their id, and carry on under the boundary's quiet
             note. Outside an organization the list is nobody's business here, and the
             boundary lets through. */
          <QueryBoundary
            isLoading={false}
            error={orgSlug ? organizations.error : null}
            hasData={organizations.data !== undefined}
          >
            {unknownOrganization ? (
              <EmptyState
                icon={<SearchX />}
                title="Organization not found"
                description="It does not exist, or you are not a member of it."
              />
            ) : (
              /* A page that throws while rendering loses only its own space: the bar and the
                 sections stay, so there is still somewhere to go, and the next address
                 renders afresh instead of inheriting the fallback from the last one. */
              <ErrorBoundary FallbackComponent={ErrorFallback} resetKeys={[location.pathname]}>
                <Suspense fallback={<PageSpinner />}>
                  <Outlet />
                </Suspense>
              </ErrorBoundary>
            )}
          </QueryBoundary>
        )}
      </main>
    </div>
  );
}
