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
    // No negative bottom margin here: the border belongs to this element, not a parent it
    // needs to overlap, and -mb-px would cancel the spacing the layout puts after it —
    // Tailwind v4 spaces children with margin-block-end, which that overrides.
    <nav aria-label="Project sections" className="flex gap-1 overflow-x-auto border-b">
      {tabs.map((tab) => {
        const active = tab.exact ? pathname === tab.to : pathname.startsWith(tab.to);

        return (
          <NavLink
            key={tab.to}
            to={tab.to}
            end={tab.exact}
            className={cn(
              "flex items-center gap-2 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors",
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
  );
}
