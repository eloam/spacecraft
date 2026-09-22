import { useParams } from "react-router-dom";
import { FolderGit2, History, KeyRound, Package, Settings, Users } from "lucide-react";
import { SectionNav } from "@/components/layout/SectionNav";

/**
 * The organization's sections.
 *
 * The sub-header that scopes a page to one organization: tabs sit under the name of what
 * they act on, rather than in the global bar, so it is always clear which organization
 * "Members" or "Credentials" refers to. The global bar keeps only what is global — the
 * product, the theme, the account — and already reads "SpaceCraft / Acme Corp", so there is
 * no title here: repeating the name said it twice on every page.
 */
export function OrganizationNav() {
  const { orgSlug = "" } = useParams();
  const base = `/orgs/${orgSlug}`;

  return (
    <div className="border-b bg-topbar">
      <div className="mx-auto max-w-6xl px-4">
        <SectionNav
          label="Sections"
          className="pt-2"
          tabs={[
            {
              label: "Projects",
              to: base,
              icon: <FolderGit2 className="size-4" aria-hidden />,
              // The organization root and everything under a project: opening a project or
              // one of its runs is still being in Projects, the way a file is still in Code.
              matches: (path) => path === base || path.startsWith(`${base}/projects`),
            },
            // Before Published: a run is what produces a publication, so the two read in the
            // order they happen.
            {
              label: "Runs",
              to: `${base}/runs`,
              icon: <History className="size-4" aria-hidden />,
              // A single run lives at /runs/{id} and belongs to this section too.
              matches: (path) => path.startsWith(`${base}/runs`),
            },
            { label: "Published", to: `${base}/publications`, icon: <Package className="size-4" aria-hidden /> },
            { label: "Credentials", to: `${base}/credentials`, icon: <KeyRound className="size-4" aria-hidden /> },
            { label: "Members", to: `${base}/members`, icon: <Users className="size-4" aria-hidden /> },
            { label: "Settings", to: `${base}/settings`, icon: <Settings className="size-4" aria-hidden /> },
          ]}
        />
      </div>
    </div>
  );
}
