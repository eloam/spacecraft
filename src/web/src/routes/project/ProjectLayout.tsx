import { useState } from "react";
import { Outlet, useParams } from "react-router-dom";
import { Play } from "lucide-react";
import { Button } from "@/components/ui/button";
import { BackLink } from "@/components/BackLink";
import { ProjectNav } from "@/components/layout/ProjectNav";
import { GenerateDialog } from "@/routes/project/GenerateDialog";
import { useProject, useSpecs } from "@/api/queries";
import { SpecValidationStatus } from "@/api/types";

/**
 * Frame for every project section: who it is, what you can do to it, and where you can go.
 *
 * "Generate SDK" lives here rather than on one section because it is the project's action,
 * not any one tab's — and it is the reason most visits happen at all.
 */
export function ProjectLayout() {
  const { orgId = "", projectId = "" } = useParams();
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);

  const [generateOpen, setGenerateOpen] = useState(false);

  const canGenerate =
    (project.data?.targets.length ?? 0) > 0 &&
    (specs.data?.some((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? false);

  return (
    <div className="space-y-6">
      {/* No breadcrumb: its only useful link was the one back to the project list, and the
          two crumbs above it repeated an organization the top bar already names. */}
      <div>
        <BackLink to={`/orgs/${orgId}`} className="mb-2">
          All projects
        </BackLink>

        {/* The link sits above this row rather than inside it, so "Generate SDK" stays level
            with the title instead of riding up beside a much smaller line. */}
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
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
