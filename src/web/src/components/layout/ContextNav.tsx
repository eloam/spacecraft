import type { ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";

export type ContextTab = {
  label: string;
  to: string;
  icon?: ReactNode;
  /**
   * Decides whether this tab is the current section, given the path.
   *
   * A tab often owns more routes than the one it links to — the Projects tab covers every
   * page under a project — and NavLink's own matching cannot express that without also
   * lighting up for sibling sections whose paths share the prefix.
   */
  matches?: (pathname: string) => boolean;
};

/**
 * The sub-header that scopes a page to one thing.
 *
 * Tabs sit under the name of what they act on, rather than in the global bar, so it is
 * always clear which organization "Members" or "Credentials" refers to. The global bar keeps
 * only what is global: the product, the theme, the account.
 *
 * Marked as its own nav landmark and separate from the top one, so assistive tech announces
 * two navigations with distinct purposes instead of one long list.
 */
export function ContextNav({
  title,
  meta,
  tabs,
}: {
  /** Optional: omit when the header above already names what these tabs act on. */
  title?: ReactNode;
  meta?: ReactNode;
  tabs: ContextTab[];
}) {
  const { pathname } = useLocation();

  return (
    <div className="border-b bg-topbar">
      <div className="mx-auto max-w-6xl px-4">
        {(title || meta) && (
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-4 pb-3">
            {title && <h1 className="font-heading text-lg font-medium tracking-tight">{title}</h1>}
            {meta}
          </div>
        )}

        <nav
          aria-label="Sections"
          className="-mb-px flex gap-1 overflow-x-auto pt-2 scroll-smooth [scrollbar-width:thin]"
        >
          {tabs.map((tab) => (
            <NavLink
              key={tab.to}
              to={tab.to}
              end
              className={({ isActive }) =>
                cn(
                  // first:pl-0 rather than a negative margin on the whole bar: both put the first
                  // tab's icon on the page margin, but this one keeps its underline from starting
                  // 12px to the left of everything else. The gap to the next tab is untouched.
                  "flex items-center gap-2 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors first:pl-0",
                  tab.matches?.(pathname) ?? isActive
                    ? "border-foreground font-medium text-foreground"
                    : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
                )
              }
            >
              {tab.icon}
              {tab.label}
            </NavLink>
          ))}
        </nav>
      </div>
    </div>
  );
}
