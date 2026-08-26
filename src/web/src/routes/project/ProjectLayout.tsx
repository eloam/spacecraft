import { useState } from "react";
import { Outlet, useParams } from "react-router-dom";
import { Play } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageBreadcrumb } from "@/components/PageBreadcrumb";
import { ProjectNav } from "@/components/layout/ProjectNav";
import { GenerateDialog } from "@/routes/project/GenerateDialog";
import { useOrganization, useProject, useSpecs } from "@/api/queries";
import { SpecValidationStatus } from "@/api/types";

/**
 * Frame for every project section: who it is, what you can do to it, and where you can go.
 *
 * "Generate SDK" lives here rather than on one section because it is the project's action,
 * not any one tab's — and it is the reason most visits happen at all.
 */
export function ProjectLayout() {
  const { orgId = "", projectId = "" } = useParams();
  const organization = useOrganization(orgId);
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);

  const [generateOpen, setGenerateOpen] = useState(false);

  const canGenerate =
    (project.data?.targets.length ?? 0) > 0 &&
    (specs.data?.some((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? false);

  return (
    <div className="space-y-6">
      <PageBreadcrumb
        crumbs={[
          { label: "Organizations", to: "/" },
          { label: organization.data?.name ?? "Organization", to: `/orgs/${orgId}` },
          { label: project.data?.name ?? "Project" },
        ]}
      />

      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {project.data?.name ?? "Project"}
          </h1>
          <p className="font-mono text-xs text-muted-foreground">{project.data?.slug}</p>
        </div>

        <Button onClick={() => setGenerateOpen(true)} disabled={!canGenerate}>
          <Play className="size-4" aria-hidden />
          Generate SDK
        </Button>
      </div>

      <ProjectNav />

      <Outlet />

      <GenerateDialog
        orgId={orgId}
        projectId={projectId}
        open={generateOpen}
        onOpenChange={setGenerateOpen}
      />
    </div>
  );
}
