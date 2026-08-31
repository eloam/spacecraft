
import { useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Check, CircleAlert, Minus } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
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
   * What the project field starts on. A project page always has one; the runs list has one
   * only when its filter names one. Either way the field is shown and can be changed — this
   * chooses its initial value, it does not hide it.
   */
  projectId?: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const navigate = useNavigate();

  const [pickedProject, setPickedProject] = useState("");
  const [specId, setSpecId] = useState("");
  const [targetId, setTargetId] = useState("");
  /**
   * null means "whatever the target says". A target set to publish automatically should
   * arrive with the switch already on, or this dialog would quietly propose the opposite of
   * what the target is configured to do; an explicit answer here overrides it for this run.
   */
  const [publish, setPublish] = useState<boolean | null>(null);

  const activeProject = pickedProject || (projectId ?? "");

  // Cleared when the project this dialog was opened for changes.
  //
  // Nothing remounts it: the project route swaps its params without replacing the layout that
  // holds this component, so a selection made on one project survived onto the next. Opening
  // it from a second project offered — and would have queued — a run for the first, with a
  // spec and a target that belong to neither the page nor the picker. The API cannot catch
  // it either: the pairing it validates is internally consistent, just not the one on screen,
  // and for an auto-publishing target that is an irreversible push to a registry.
  const openedFor = useRef(projectId);
  if (openedFor.current !== projectId) {
    openedFor.current = projectId;

    if (pickedProject) setPickedProject("");
    if (specId) setSpecId("");
    if (targetId) setTargetId("");
    if (publish !== null) setPublish(null);
  }
  // Only fetched when the dialog has to ask; a project page already knows the answer.
  const projects = useProjects(orgId);

  const project = useProject(orgId, activeProject);
  const specs = useSpecs(orgId, activeProject);
  // Already loaded for the switcher in the top bar, so the caller's role costs no request.
  const organizations = useOrganizations();
  const createRun = useCreateRun(orgId);

  const usableSpecs = specs.data?.items.filter((s) => s.validationStatus !== SpecValidationStatus.Invalid) ?? [];
  const targets = project.data?.targets ?? [];


  const selectedSpec = specId || usableSpecs[0]?.id || "";
  const selectedTarget = targetId || targets[0]?.id || "";
  const target = targets.find((t) => t.id === selectedTarget);

  /**
   * Why this run would fail before it did anything, said here rather than by a toast.
   *
   * A version can be publishable to one registry and refused by another — 1.2.3.4 is ordinary
   * for NuGet and inexpressible in npm — so the document is valid, the target is fine, and
   * this pairing will still fail. Said here so the ledger below is read knowing it, and not
   * used to disable anything: the run is queued and fails at validation, which is the step
   * that exists to judge exactly this and the only outcome that leaves a record behind.
   */
  const versionRefusal = target
    ? (usableSpecs
        .find((s) => s.id === selectedSpec)
        ?.publishesAs.find((r: { registry: number }) => r.registry === target.registry)
        ?.refusal ?? null)
    : null;

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

  // Read, not reset: switching to a target that publishes nowhere must not leave a switch on
  // behind promising something the queued run would then quietly skip.
  const wantsPublish = publish ?? (target?.autoPublish ?? false);
  const willPublish = wantsPublish && !publishBlocked;
  const overridden = publish !== null && target !== undefined && publish !== target.autoPublish;

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
            {/* Always shown, even when the caller names one: a filter that silently becomes
                the subject of a dialog is a decision the reader cannot see, and cannot
                change without closing it. Preselected, not hidden. */}
            <Field>
                <FieldLabel htmlFor="project">Project</FieldLabel>
                <Select
                  value={activeProject}
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
                      {(projects.data ?? []).find((p) => p.slug === activeProject)?.name ??
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

            <Field data-disabled={!activeProject || undefined}>
              <FieldLabel htmlFor="spec">Spec version</FieldLabel>
              <Select
                value={selectedSpec}
                disabled={!activeProject}
                onValueChange={(value) => setSpecId(String(value))}
              >
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

            <Field data-disabled={!activeProject || undefined}>
              <FieldLabel htmlFor="target">Target</FieldLabel>
              <Select
                value={selectedTarget}
                disabled={!activeProject}
                onValueChange={(value) => {
                  setTargetId(String(value));
                  // Back to following the new target: an override belongs to the target it
                  // was set against, not to the dialog.
                  setPublish(null);
                }}
              >
                <SelectTrigger id="target" className="w-full">
                  <SelectValue>
                    {target
                      ? `${languageLabel[target.language]} — ${target.packageName} · ${target.autoPublish ? "Automatic" : "Manual"}`
                      : ""}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {targets.map((t) => (
                    <SelectItem key={t.id} value={t.id}>
                      <span className="flex min-w-0 items-center gap-2">
                        <span className="truncate">
                          {languageLabel[t.language]} — {t.packageName}
                        </span>
                        {/* How this target publishes, where it is chosen: it decides what
                            the switch below arrives set to, and picking blind then finding
                            the switch already on reads as the dialog deciding for you. */}
                        <Badge variant="outline" className="shrink-0 text-[10px]">
                          {t.autoPublish ? "Automatic" : "Manual"}
                        </Badge>
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            {/* What the run will do, in the shape the delete dialogs use for what a delete
                costs. The steps were only ever named in one line of prose at the top, and
                the one that cannot be undone read the same as the three that can. */}
            {/* Beside the ledger rather than instead of it, and it does not stop the run.
                The pipeline's first step is validation, and this is exactly what it exists to
                judge: the run is queued, fails there, and leaves a record saying why. A
                refusal here would be quieter — no run, no history, nothing for anyone waiting
                on a publish to find. */}
            {versionRefusal && (
              <div className="space-y-1.5 rounded-2xl border border-destructive/40 bg-destructive/5 p-3.5 text-sm">
                <p className="text-xs font-medium tracking-wider text-destructive uppercase">
                  This run cannot start
                </p>
                <p className="text-muted-foreground">{versionRefusal}</p>
              </div>
            )}

            {target && (
              <div className="space-y-3 rounded-2xl border bg-muted/40 p-3.5 text-sm">
                <div className="space-y-1.5">
                  <p className="text-xs font-medium tracking-wider text-muted-foreground uppercase">
                    This run
                  </p>
                  <ul className="space-y-1.5">
                    <li className="flex items-start gap-2.5">
                      <Check className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" aria-hidden />
                      <span>
                        Validates the document, generates {target.packageName} and builds it
                      </span>
                    </li>
                    <li className="flex items-start gap-2.5">
                      {willPublish ? (
                        <>
                          <CircleAlert className="mt-0.5 size-3.5 shrink-0 text-destructive" aria-hidden />
                          <span>
                            Publishes to {registryLabel[target.registry]} — a version accepted
                            by a registry cannot be taken back
                          </span>
                        </>
                      ) : (
                        <>
                          <Minus className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" aria-hidden />
                          <span className="text-muted-foreground">
                            Publishes nothing. The build stays here, downloadable from the run.
                          </span>
                        </>
                      )}
                    </li>
                  </ul>
                </div>
              </div>
            )}

            {/* A switch, like the auto-publish toggle on the SDK target it names: both answer
                "does this go to a registry", and answering it two different ways on two
                screens made them look like different questions. Content first and the
                control last, the way that row reads.

                data-disabled as well as a disabled switch, because Field dims its own label
                from it — the control greying out alone left the sentence beside it reading
                as an available choice. */}
            {/* With the ledger, not before it: with no target chosen, publishBlocked reads
                "no-credential" from an undefined target, and the row announced that "this
                target publishes nowhere" about a target nobody had picked. */}
            {target && (
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
                  ) : overridden ? (
                    // The override is the point of the switch, so it says which way it went
                    // rather than repeating the warning the ledger above already carries.
                    target?.autoPublish
                      ? "Overriding this target: it publishes automatically, and this one run will not."
                      : "Overriding this target: it publishes manually, and this one run will."
                  ) : target?.autoPublish ? (
                    "Following this target, which publishes automatically. Turn it off to generate and build only."
                  ) : (
                    "Following this target, which publishes manually. Turn it on to publish this one run."
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
            )}
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
