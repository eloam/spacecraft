import { NavLink, useLocation } from "react-router-dom";
import { Bell, KeyRound, ShieldAlert, UserRound } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * The account's sections, in the same shape organizations and projects use.
 *
 * Splitting one long page into routed sections means a link can point at the part being
 * discussed — "change your password" can land on the password form rather than the top of a
 * page the reader then has to scan.
 */
export function AccountNav() {
  const { pathname } = useLocation();

  const tabs = [
    { label: "Profile", to: "/account", icon: <UserRound className="size-4" aria-hidden />, exact: true },
    { label: "Security", to: "/account/security", icon: <KeyRound className="size-4" aria-hidden /> },
    { label: "Notifications", to: "/account/notifications", icon: <Bell className="size-4" aria-hidden /> },
    { label: "Danger zone", to: "/account/danger", icon: <ShieldAlert className="size-4" aria-hidden /> },
  ];

  return (
    // The rule sits on the wrapper rather than the nav, so -mb-px can lift the active tab's
    // underline over it instead of stacking the two. It is safe there: on the nav it cannot
    // cancel the margin-block-end the layout puts after the wrapper, which is what it did
    // when this element carried the border itself.
    <div className="border-b">
      <nav aria-label="Account sections" className="-mb-px flex gap-1 overflow-x-auto scroll-smooth [scrollbar-width:thin]">
        {tabs.map((tab) => {
          const active = tab.exact ? pathname === tab.to : pathname.startsWith(tab.to);

          return (
            <NavLink
              key={tab.to}
              to={tab.to}
              end={tab.exact}
              className={cn(
                // first:pl-0 rather than a negative margin on the whole bar: both put the first
                // tab's icon on the page margin, but this one keeps its underline from starting
                // 12px to the left of everything else. The gap to the next tab is untouched.
                "flex items-center gap-2 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors first:pl-0",
                active
                  ? "border-foreground font-medium text-foreground"
                  : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
              )}
            >
              {tab.icon}
              {tab.label}
            </NavLink>
          );
        })}
      </nav>
    </div>
  );
}
