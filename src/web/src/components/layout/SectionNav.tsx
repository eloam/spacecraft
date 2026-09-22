import { useEffect, useRef, type ReactNode } from "react";
import { Link, matchPath, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";

export type SectionTab = {
  label: string;
  to: string;
  icon?: ReactNode;
  /**
   * Whether the tab is current only at its own address, or at everything under it. The
   * default is "under it", which is what a section is; the first tab of a strip usually
   * links to the strip's root and has to be exact, or it would be current everywhere.
   */
  end?: boolean;
  /**
   * Decides whether this tab is the current section, given the path, when `to` and `end`
   * cannot: the Projects tab owns the organization root and everything under a project,
   * which is neither the root alone nor everything under it.
   */
  matches?: (pathname: string) => boolean;
};

/**
 * A strip of section tabs, one per routed part of an organization, a project or the account.
 *
 * One component rather than three copies: the copies had already drifted — one derived the
 * active style from its own predicate but left `aria-current` to NavLink's exact match, so
 * the tab that looked current was not announced as such, and the others recomputed with
 * `pathname.startsWith` what the router matches for them. Here the one predicate decides
 * both the style and `aria-current`, through `matchPath`, which is what NavLink runs.
 *
 * Each strip is its own nav landmark, named by the caller, so assistive tech announces two
 * navigations with distinct purposes instead of one long list.
 */
export function SectionNav({
  label,
  tabs,
  className,
}: {
  /** The landmark's name: "Sections", "Project sections", "Account sections". */
  label: string;
  tabs: SectionTab[];
  className?: string;
}) {
  const { pathname } = useLocation();
  const current = useRef<HTMLAnchorElement>(null);

  // The strip scrolls sideways on a phone, and nothing scrolled the current tab into view:
  // the organization's last three tabs were current off-screen. `nearest` keeps the page
  // from jumping vertically; the strip's own scroll-smooth animates the move.
  useEffect(() => {
    current.current?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [pathname]);

  return (
    <nav
      aria-label={label}
      className={cn(
        "-mb-px flex [scrollbar-width:thin] gap-1 overflow-x-auto scroll-smooth",
        className,
      )}
    >
      {tabs.map((tab) => {
        const active = tab.matches
          ? tab.matches(pathname)
          : matchPath({ path: tab.to, end: tab.end ?? false }, pathname) !== null;

        return (
          <Link
            key={tab.to}
            ref={active ? current : undefined}
            to={tab.to}
            aria-current={active ? "page" : undefined}
            className={cn(
              // first:pl-0 rather than a negative margin on the whole bar: both put the first
              // tab's icon on the page margin, but this one keeps its underline from starting
              // 12px to the left of everything else. The gap to the next tab is untouched.
              "flex items-center gap-2 border-b-2 px-3 py-2 text-sm whitespace-nowrap transition-colors first:pl-0",
              active
                ? "border-foreground font-medium text-foreground"
                : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
            )}
          >
            {tab.icon}
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}
