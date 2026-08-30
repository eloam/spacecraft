
import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Field, FieldContent, FieldDescription, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useCreateRun, useOrganizations, useProject, useProjects, useSpecs } from "@/api/queries";
import {
  OrganizationRole, SpecValidationStatus, StepKind, languageLabel, registryLabel, stepKindLabel,
} from "@/api/types";

export function GenerateDialog({
  orgId,
  projectId,
  open,
  onOpenChange,
}: {
  orgId: string;
  /**
   * Fixed by the caller when it has one — a project page always does. The runs list does not
   * unless its filter names one, so it passes nothing and the dialog asks first.
   */
  projectId?: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const navigate = useNavigate();

  const [pickedProject, setPickedProject] = useState("");
  const activeProject = projectId ?? pickedProject;
  // Only fetched when the dialog has to ask; a project page already knows the answer.
  const projects = useProjects(orgId);

  const project = useProject(orgId, activeProject);
  const specs = useSpecs(orgId, activeProject);
  // Already loaded for the switcher in the top bar, so the caller's role costs no request.
  const organizations = useOrganizations();
  const createRun = useCreateRun(orgId);

  const usableSpecs = specs.data?.items.filter((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? [];
  const targets = project.data?.targets ?? [];

  const [specId, setSpecId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [publish, setPublish] = useState(false);

  const selectedSpec = specId || usableSpecs[0]?.id || "";
  const selectedTarget = targetId || targets[0]?.id || "";
  const target = targets.find((t) => t.id === selectedTarget);

  // Slug or id: the route carries the slug, but links minted before that still carry the id.
  const role = organizations.data?.find((o) => o.slug === orgId || o.id === orgId)?.role;

  /**
   * Why this run cannot publish, said before it is queued rather than after.
   *
   * Both refusals are expensive to discover late: the API demands Admin for a publishing run
   * (RunEndpoints.CreateAsync) and answers a Member with a 403 toast, and a target with no
   * credential fails at the publish step — after validate, generate and build have all run.
   * An unknown role means the organizations query has not landed; the server still decides.
   *
   * Role first: setting a credential on a target is itself an Admin action, so telling a
   * Member to go and set one sends them to a form that would refuse them too.
   */
  const publishBlocked =
    role !== undefined && role < OrganizationRole.Admin
      ? "not-admin"
      : !target?.registryCredentialId
        ? "no-credential"
        : null;

  // Read, not reset: switching to a target that publishes nowhere must not leave a ticked box
  // behind promising something the queued run would then quietly skip.
  const willPublish = publish && !publishBlocked;

  // Named from what will actually run, so the summary at the top and the box below it cannot
  // disagree — it used to promise "publish" on every run, including the ones that do not.
  const steps = [
    StepKind.Validate,
    StepKind.Generate,
    StepKind.Build,
    ...(willPublish ? [StepKind.Publish] : []),
  ]
    .map((kind) => stepKindLabel[kind].toLowerCase())
    .join(" → ");

  function submit(event: React.FormEvent) {
    event.preventDefault();
    createRun.mutate(
      { specVersionId: selectedSpec, sdkTargetId: selectedTarget, publish: willPublish },
      {
        onSuccess: (run) => {
          onOpenChange(false);
          // Queuing a run is the point of this dialog; leaving the user on the tab they
          // started from means the thing they asked for happens somewhere they cannot see.
          navigate(`/orgs/${orgId}/runs/${run.id}`);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not queue the run."),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Generate an SDK</DialogTitle>
            <DialogDescription>Runs {steps}.</DialogDescription>
          </DialogHeader>

          <FieldGroup className="py-4">
            {projectId === undefined && (
              <Field>
                <FieldLabel htmlFor="project">Project</FieldLabel>
                <select
                  id="project"
                  value={pickedProject}
                  onChange={(e) => {
                    setPickedProject(e.target.value);
                    // A spec and a target both belong to a project. Carried across, they
                    // would name rows the newly chosen one does not own.
                    setSpecId("");
                    setTargetId("");
                  }}
                  className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
                >
                  <option value="">Choose a project…</option>
                  {(projects.data ?? []).map((p) => (
                    <option key={p.id} value={p.slug}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </Field>
            )}

            <Field>
              <FieldLabel htmlFor="spec">Spec version</FieldLabel>
              <select
                id="spec"
                value={selectedSpec}
                onChange={(e) => setSpecId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {usableSpecs.map((spec) => (
                  <option key={spec.id} value={spec.id}>
                    #{spec.sequence} — {spec.apiTitle ?? "untitled"} {spec.apiVersion ?? ""}
                  </option>
                ))}
              </select>
            </Field>

            <Field>
              <FieldLabel htmlFor="target">Target</FieldLabel>
              <select
                id="target"
                value={selectedTarget}
                onChange={(e) => setTargetId(e.target.value)}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {targets.map((t) => (
                  <option key={t.id} value={t.id}>
                    {languageLabel[t.language]} — {t.packageName}
                  </option>
                ))}
              </select>
            </Field>

            {/* The Checkbox primitive, like every other tick in the app: a bare input beside
                styled fields read as part of the page's furniture rather than a choice.
                data-disabled as well as a disabled box, because Field dims its own label
                from it — a 16px square greying out alone left the sentence beside it
                reading as an available choice. */}
            <Field orientation="horizontal" data-disabled={Boolean(publishBlocked) || undefined}>
              <Checkbox
                id="publish"
                checked={willPublish}
                disabled={Boolean(publishBlocked)}
                onCheckedChange={(checked) => setPublish(checked)}
              />
              <FieldContent>
                <FieldLabel htmlFor="publish">
                  {/* The destination, not the abstraction: "the registry" gave no way to tell
                      what this run would push, or where. */}
                  {target
                    ? `Publish ${target.packageName} to ${registryLabel[target.registry]} after a successful build`
                    : "Publish to the registry after a successful build"}
                </FieldLabel>
                <FieldDescription>
                  {publishBlocked === "no-credential" ? (
                    <>
                      This target publishes nowhere. Choose a credential under Publish with on
                      the{" "}
                      <Link
                        to={`/orgs/${orgId}/projects/${activeProject}/targets`}
                        onClick={() => onOpenChange(false)}
                      >
                        SDK targets tab
                      </Link>
                      .
                    </>
                  ) : publishBlocked === "not-admin" ? (
                    "Publishing to a registry cannot be undone, so it takes an admin. This run generates and builds only."
                  ) : (
                    `Pushing to ${target ? registryLabel[target.registry] : "a registry"} cannot be undone once it is accepted.`
                  )}
                </FieldDescription>
              </FieldContent>
            </Field>
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            {/* The irreversible variant says so on the button: ticking a box eight lines up
                should not leave the two runs looking like the same click. */}
            <Button type="submit" disabled={!selectedSpec || !selectedTarget || createRun.isPending}>
              {createRun.isPending
                ? "Queueing…"
                : willPublish
                  ? "Queue run and publish"
                  : "Queue run"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The findings behind a spec's warning count.
 *
 * A large document produces a lot of them — the Keycloak Admin API yields 413 — so the list
 * is virtualised by nothing more than a scroll container, and only fetched once opened.
 */
