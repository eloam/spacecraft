import { useParams } from "react-router-dom";
import { Building2, FolderGit2, KeyRound, Package, Settings, Users } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { ContextNav } from "@/components/layout/ContextNav";
import { useOrganization } from "@/api/queries";
import { roleLabel } from "@/api/types";

/** The organization's own sections, shown on every page scoped to it. */
export function OrganizationNav() {
  const { orgId = "" } = useParams();
  const organization = useOrganization(orgId);

  const base = `/orgs/${orgId}`;

  return (
    <ContextNav
      title={
        <span className="flex items-center gap-2">
          <Building2 className="size-4 text-muted-foreground" aria-hidden />
          {/* Falls back to the slug while loading: it is already in the URL, so showing it
              keeps the header from jumping once the name arrives. */}
          {organization.data?.name ?? orgId}
        </span>
      }
      meta={
        organization.data && (
          <Badge variant="secondary">{roleLabel[organization.data.role] ?? "Member"}</Badge>
        )
      }
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
