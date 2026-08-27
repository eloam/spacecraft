import { useParams } from "react-router-dom";
import { FolderGit2, KeyRound, Package, Settings, Users } from "lucide-react";
import { ContextNav } from "@/components/layout/ContextNav";

/**
 * The organization's sections.
 *
 * No title of its own: the header above already reads "SpaceCraft / Acme Corp", and
 * repeating the name here said it twice on every page.
 */
export function OrganizationNav() {
  const { orgId = "" } = useParams();
  const base = `/orgs/${orgId}`;

  return (
    <ContextNav
      tabs={[
        {
          label: "Projects",
          to: base,
          icon: <FolderGit2 className="size-4" aria-hidden />,
          // The organization root and everything under a project: opening a project or one
          // of its runs is still being in Projects, the way a file is still in Code.
          matches: (path) => path === base || path.startsWith(`${base}/projects`),
        },
        { label: "Published", to: `${base}/publications`, icon: <Package className="size-4" aria-hidden /> },
        { label: "Credentials", to: `${base}/credentials`, icon: <KeyRound className="size-4" aria-hidden /> },
        { label: "Members", to: `${base}/members`, icon: <Users className="size-4" aria-hidden /> },
        { label: "Settings", to: `${base}/settings`, icon: <Settings className="size-4" aria-hidden /> },
      ]}
    />
  );
}
