import { useParams } from "react-router-dom";
import { FileCode2, History, Package, Settings } from "lucide-react";
import { NavLink, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";

/**
 * The project's sections.
 *
 * Rendered as routes rather than local tab state, so a section can be linked to, opened in
 * a new tab and reached with the browser's back button — and so settings can sit alongside
 * the others instead of being a lone link among tabs that behave differently.
 */
export function ProjectNav() {
  const { orgId = "", projectId = "" } = useParams();
  const { pathname } = useLocation();

  const base = `/orgs/${orgId}/projects/${projectId}`;

  const tabs = [
    { label: "Specs", to: base, icon: <FileCode2 className="size-4" aria-hidden />, exact: true },
    { label: "SDK targets", to: `${base}/targets`, icon: <Package className="size-4" aria-hidden /> },
    { label: "Runs", to: `${base}/runs`, icon: <History className="size-4" aria-hidden /> },
    { label: "Settings", to: `${base}/settings`, icon: <Settings className="size-4" aria-hidden /> },
  ];

  return (
    // The rule sits on the wrapper rather than the nav, so -mb-px can lift the active tab's
    // underline over it instead of stacking the two. It is safe there: on the nav it cannot
    // cancel the margin-block-end the layout puts after the wrapper, which is what it did
    // when this element carried the border itself.
    <div className="border-b">
      <nav aria-label="Project sections" className="-mb-px flex gap-1 overflow-x-auto">
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
