
import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import {
  Field, FieldContent, FieldDescription, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { ApiError } from "@/api/client";
import { useCreateRun, useOrganizations, useProject, useProjects, useSpecs } from "@/api/queries";
import {
  OrganizationRole, SpecValidationStatus, StepKind, languageLabel, registryLabel, stepKindLabel,
} from "@/api/types";

/** Written once, so the closed trigger and the open list cannot drift apart. */
function specLabel(spec?: { sequence: number; apiTitle: string | null; apiVersion: string | null }) {
  if (!spec) return "";
  return `#${spec.sequence} — ${spec.apiTitle ?? "untitled"} ${spec.apiVersion ?? ""}`.trim();
}

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
            {/* Every SelectValue carries its own children. Base UI falls back to the raw
                value when it cannot resolve the selected item — a guid, in two of these
                three — and it cannot resolve one whose children are not a plain string. */}
            {projectId === undefined && (
              <Field>
                <FieldLabel htmlFor="project">Project</FieldLabel>
                <Select
                  value={pickedProject}
                  onValueChange={(value) => {
                    setPickedProject(String(value));
                    // A spec and a target both belong to a project. Carried across, they
                    // would name rows the newly chosen one does not own.
                    setSpecId("");
                    setTargetId("");
                  }}
                >
                  <SelectTrigger id="project" className="w-full">
                    <SelectValue>
                      {(projects.data ?? []).find((p) => p.slug === pickedProject)?.name ??
                        "Choose a project…"}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {(projects.data ?? []).map((p) => (
                      <SelectItem key={p.id} value={p.slug}>
                        {p.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </Field>
            )}

            <Field>
              <FieldLabel htmlFor="spec">Spec version</FieldLabel>
              <Select value={selectedSpec} onValueChange={(value) => setSpecId(String(value))}>
                <SelectTrigger id="spec" className="w-full">
                  <SelectValue>{specLabel(usableSpecs.find((s) => s.id === selectedSpec))}</SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {usableSpecs.map((spec) => (
                    <SelectItem key={spec.id} value={spec.id}>
                      {specLabel(spec)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            <Field>
              <FieldLabel htmlFor="target">Target</FieldLabel>
              <Select value={selectedTarget} onValueChange={(value) => setTargetId(String(value))}>
                <SelectTrigger id="target" className="w-full">
                  <SelectValue>
                    {target ? `${languageLabel[target.language]} — ${target.packageName}` : ""}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {targets.map((t) => (
                    <SelectItem key={t.id} value={t.id}>
                      {languageLabel[t.language]} — {t.packageName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            {/* A switch, like the auto-publish toggle on the SDK target it names: both answer
                "does this go to a registry", and answering it two different ways on two
                screens made them look like different questions. Content first and the
                control last, the way that row reads.

                data-disabled as well as a disabled switch, because Field dims its own label
                from it — the control greying out alone left the sentence beside it reading
                as an available choice. */}
            <Field
              orientation="horizontal"
              className="justify-between gap-6"
              data-disabled={Boolean(publishBlocked) || undefined}
            >
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
              <Switch
                id="publish"
                checked={willPublish}
                disabled={Boolean(publishBlocked)}
                onCheckedChange={(checked) => setPublish(checked)}
              />
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
