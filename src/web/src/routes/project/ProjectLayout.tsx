import { useState } from "react";
import { Link, Outlet, useParams } from "react-router-dom";
import { useOrganizationId, useProjectId } from "@/hooks/useRouteIdentity";
import { Play, SearchX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { BackLink } from "@/components/BackLink";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ProjectNav } from "@/components/layout/ProjectNav";
import { GenerateDialog } from "@/components/GenerateDialog";
import { useProject, useProjects, useSpecs } from "@/api/queries";
import { isGeneratable } from "@/api/types";

/**
 * Frame for every project section: who it is, what you can do to it, and where you can go.
 *
 * "Generate SDK" lives here rather than on one section because it is the project's action,
 * not any one tab's — and it is the reason most visits happen at all.
 */
export function ProjectLayout() {
  const { orgId: orgSlug = "", projectId: projectSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const projects = useProjects(orgId);
  const projectId = useProjectId(orgId, projectSlug);
  const project = useProject(orgId, projectId);
  const specs = useSpecs(orgId, projectId);

  const [generateOpen, setGenerateOpen] = useState(false);

  const base = `/orgs/${orgSlug}/projects/${projectSlug}`;
  const linkClass = "font-medium text-foreground underline underline-offset-4";

  // Unresolved is not the same as absent, and absent is not the same as unknown. Until the
  // project list has arrived, projectId is empty, its queries are disabled, and an action
  // fired now would name no project at all. Once the list is here and the slug is still not
  // in it, nothing will ever enable them: judged on `!projectId` alone, that was a spinner
  // turning for ever over sections bound to a project called "". An address that carries the
  // id is passed through as it is, so an unknown id is not caught here — it reaches the
  // project query and comes back as its 404, through the error path below.
  const resolvingProject = !projectId && projects.isPending;
  const unknownProject = !projectId && projects.isSuccess;
  // And a list that never loaded says nothing about the slug: neither resolving nor unknown,
  // it is the failure itself, shown in the frame's place.
  const failedProjects = !projectId && projects.isError;

  // `isPending` rather than `isLoading`: a disabled query is pending and not fetching, and
  // isLoading is the two together — false for the project queries while the id is still on
  // its way, which is exactly when the spinner has to stay on.
  const loading = resolvingProject || project.isPending || specs.isPending;
  const hasTarget = (project.data?.targets.length ?? 0) > 0;
  const hasUsableSpec =
    specs.data?.items.some(isGeneratable) ?? false;
  const canGenerate = hasTarget && hasUsableSpec;

  /**
   * Why the button is off, in the words of the thing that would turn it on.
   *
   * Both queries resolve to nothing while they load and after they fail, so `canGenerate` is
   * false in four different situations that used to look identical: a greyed primary action
   * with no spinner, no message, and no hint that the missing half lives on another tab.
   * A disabled button swallows pointer events, so the reason is said in the open rather than
   * in a tooltip nobody can open.
   */
  const reason = (() => {
    // Nothing to explain when the button works, and nothing to say yet while the queries are
    // out — the button carries a spinner meanwhile.
    if (canGenerate || loading) return null;

    // Only when the answer is genuinely absent. React Query keeps the last one through a
    // failed refetch, and that one is enough to name the missing half — "could not be
    // loaded" under a title and slug the page is already showing contradicts itself, and
    // the section below says on its own that the last refresh failed.
    if ((project.isError && !project.data) || (specs.isError && !specs.data)) {
      return "This project could not be loaded. Reload the page to try again.";
    }

    if (!hasTarget && !hasUsableSpec) {
      return (
        <>
          <Link to={base} className={linkClass}>
            Upload a document
          </Link>{" "}
          and{" "}
          <Link to={`${base}/targets`} className={linkClass}>
            add an SDK target
          </Link>{" "}
          first
        </>
      );
    }

    if (!hasTarget) {
      return (
        <>
          <Link to={`${base}/targets`} className={linkClass}>
            Add an SDK target
          </Link>{" "}
          first — it says which language to emit
        </>
      );
    }

    // Also the wording when a document is there and failed: there is no editor here, so a
    // corrected document is uploaded rather than fixed in place.
    if (!hasUsableSpec) {
      return (
        <>
          <Link to={base} className={linkClass}>
            Upload a document
          </Link>{" "}
          that passes validation first
        </>
      );
    }

    return null;
  })();

  // Nothing inside is rendered until the slug has an id behind it, as the shell does for the
  // organization: the sections take the id as a prop, and the specs tab mounted its drop zone
  // bound to a project called "" for the length of the lookup.
  if (resolvingProject) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="size-5" />
      </div>
    );
  }

  // Worded for the two things a stale link can mean; the organization's Projects tab above
  // is the way back to the list.
  if (failedProjects) {
    return (
      <QueryBoundary isLoading={false} error={projects.error}>
        {null}
      </QueryBoundary>
    );
  }

  if (unknownProject) {
    return (
      <EmptyState
        icon={<SearchX />}
        title="Project not found"
        description="It does not exist in this organization, or it was renamed."
      />
    );
  }

  return (
    <div className="space-y-6">
      {/* No breadcrumb: its only useful link was the one back to the project list, and the
          two crumbs above it repeated an organization the top bar already names. */}
      <div>
        <BackLink to={`/orgs/${orgSlug}`} className="mb-2">
          All projects
        </BackLink>

        {/* The link sits above this row rather than inside it, so "Generate SDK" stays level
            with the title instead of riding up beside a much smaller line. */}
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="font-heading text-2xl font-medium tracking-tight">
              {project.data?.name ?? "Project"}
            </h1>
            <p className="font-mono text-xs text-muted-foreground">{project.data?.slug}</p>
          </div>

          {/* A column, so the reason sits under the button instead of squeezing the title. */}
          <div className="flex shrink-0 flex-col items-end gap-1.5">
            <Button
              onClick={() => setGenerateOpen(true)}
              disabled={!canGenerate}
              aria-describedby={reason ? "generate-blocked" : undefined}
            >
              {/* The spinner replaces the icon rather than the label: the button keeps its
                  width, so the header does not resize the moment the queries land. */}
              {loading ? <Spinner /> : <Play className="size-4" aria-hidden />}
              Generate SDK
            </Button>

            {reason && (
              <p id="generate-blocked" className="max-w-64 text-right text-xs text-muted-foreground">
                {reason}
              </p>
            )}
          </div>
        </div>
      </div>

      <ProjectNav />

      <Outlet />

      <GenerateDialog
        key={projectSlug}
        orgId={orgId}
        orgSlug={orgSlug}
        projectSlug={projectSlug}
        open={generateOpen}
        onOpenChange={setGenerateOpen}
      />
    </div>
  );
}
