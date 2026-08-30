import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ArrowRight, Ban, KeyRound, MoreHorizontal, Package, Plus, RotateCcw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import {
  Field, FieldDescription, FieldError, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import { Card, CardContent } from "@/components/ui/card";
import {
  Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle,
} from "@/components/ui/item";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import {
  useCreateCredential,
  useCredentials,
  useDeleteCredential,
  useRestoreCredential,
  useRevokeCredential,
} from "@/api/queries";
import { timeAgo } from "@/lib/format";
import { RegistryKind, languageLabel, registryLabel } from "@/api/types";
// Named explicitly: the DOM has its own Credential, and the global would win.
import type { Credential } from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

/** The public npm registry: the default, and what the field shows until told otherwise. */
const NPM_REGISTRY = "https://registry.npmjs.org";

export function CredentialsPage() {
  const { orgId = "" } = useParams();
  const { data, isLoading, error } = useCredentials(orgId);
  const revoke = useRevokeCredential(orgId);
  const restore = useRestoreCredential(orgId);
  const remove = useDeleteCredential(orgId);
  const [deleting, setDeleting] = useState<Credential | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [revoking, setRevoking] = useState<Credential | null>(null);
  const [open, setOpen] = useState(false);

  function revokeNow(credential: Credential) {
    revoke.mutate(credential.id, {
      onSuccess: () => {
        toast.success("Credential revoked");
        setRevoking(null);
      },
      onError: () => toast.error("Could not revoke the credential."),
    });
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-medium tracking-tight">Credentials</h1>
          <p className="text-sm text-muted-foreground">
            Tokens used to publish generated SDKs. Encrypted at rest and never shown again.
          </p>
        </div>
        <Button onClick={() => setOpen(true)}>
          <Plus className="size-4" aria-hidden />
          Add credential
        </Button>
      </div>

      <Alert>
        <KeyRound className="size-4" aria-hidden />
        <AlertTitle>Use automation tokens</AlertTitle>
        <AlertDescription>
          On npm, only an automation token publishes unattended — a classic token on a
          2FA-protected account will prompt for a one-time code that no pipeline can answer.
        </AlertDescription>
      </Alert>

      <QueryBoundary isLoading={isLoading} error={error} hasData={data !== undefined}>
        {data?.length ? (
          // Framed like the specs and targets tables: one card, one banded header.
          <Card className="py-0">
            <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Kind</TableHead>
                <TableHead>Label</TableHead>
                {/* "Registry", the name the field that sets it already uses. "Publishes to"
                    named a destination choice on the targets table and a URL here. */}
                <TableHead>Registry</TableHead>
                <TableHead>Token</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Last used</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((credential) => (
                <TableRow key={credential.id}>
                  <TableCell>{registryLabel[credential.registry]}</TableCell>
                  <TableCell>{credential.label}</TableCell>
                  {/* The destination is a property of the token, so it belongs beside it: two
                      npm credentials in one organization can point at different registries.
                      Capped and truncated: a registry path can be arbitrarily long, and one
                      of them widened the table until every other column needed scrolling. */}
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    <Tooltip>
                      {/* A button, not a span: Base UI's tooltip opens on focus as well as
                          hover, but a span takes no focus, so the truncated half of the URL
                          was reachable with a mouse and by no other means. */}
                      <TooltipTrigger
                        render={
                          <button
                            type="button"
                            className="block max-w-[14rem] truncate rounded-sm text-left focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none xl:max-w-[24rem]"
                          />
                        }
                      >
                        {credential.registryUrl}
                      </TooltipTrigger>
                      {/* break-all, because the tooltip exists to show the whole URL: a
                          registry address has no spaces to wrap at, so max-w-xs would clip it
                          at a third of its length and quietly claim that was all of it. */}
                      <TooltipContent className="break-all">
                        {credential.registryUrl}
                      </TooltipContent>
                    </Tooltip>
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    ••••{credential.lastFour}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-col items-start gap-1">
                      <Badge variant={credential.isRevoked ? "destructive" : "outline"}>
                        {credential.isRevoked ? "Revoked" : "Active"}
                      </Badge>
                      {/* A revoked credential its targets still name is the expensive state:
                          every run reaches the publish step and fails there, and nothing
                          says so outside each project's own runs. Counted here because this
                          is the page where the revoking happened. */}
                      {credential.usedBy.length > 0 && (
                        <span
                          className={
                            credential.isRevoked
                              ? "text-xs text-[var(--code-warning)]"
                              : "text-xs text-muted-foreground"
                          }
                        >
                          {credential.usedBy.length} target
                          {credential.usedBy.length === 1 ? "" : "s"}
                          {credential.isRevoked ? " blocked" : ""}
                        </span>
                      )}
                    </div>
                  </TableCell>
                  {/* Relative, like the specs table: this column is scanned for recency, and
                      a full timestamp on every row cost more width than it was worth. */}
                  <TableCell className="text-xs whitespace-nowrap text-muted-foreground">
                    {credential.lastUsedAt ? (
                      <Tooltip>
                        <TooltipTrigger render={<span />}>
                          {timeAgo(credential.lastUsedAt)}
                        </TooltipTrigger>
                        <TooltipContent>
                          {new Date(credential.lastUsedAt).toLocaleString()}
                        </TooltipContent>
                      </Tooltip>
                    ) : (
                      "—"
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    <DropdownMenu>
                      <DropdownMenuTrigger
                        render={<Button size="icon-sm" variant="ghost" />}
                        aria-label={`Actions for ${credential.label}`}
                      >
                        <MoreHorizontal className="size-4" aria-hidden />
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-56">
                        {credential.isRevoked ? (
                          // Revoking never destroyed the token, so undoing it is honest.
                          <DropdownMenuItem
                            onClick={() =>
                              restore.mutate(credential.id, {
                                onSuccess: () => toast.success("Credential restored"),
                                onError: () => toast.error("Could not restore the credential."),
                              })
                            }
                          >
                            <RotateCcw className="size-4" aria-hidden />
                            Restore
                          </DropdownMenuItem>
                        ) : (
                          <DropdownMenuItem
                            onClick={() => {
                              // Asked about only when something is bound to the token:
                              // revoking is the action that takes effect immediately, and
                              // every target publishing with it starts failing at its publish
                              // step. With nothing bound there is nothing to warn about.
                              if (credential.usedBy.length > 0) {
                                setRevoking(credential);
                                return;
                              }
                              revokeNow(credential);
                            }}
                          >
                            <Ban className="size-4" aria-hidden />
                            Revoke
                          </DropdownMenuItem>
                        )}

                        <DropdownMenuSeparator />

                        <DropdownMenuItem
                          variant="destructive"
                          onClick={() => {
                            setDeleteError(null);
                            setDeleting(credential);
                          }}
                        >
                          <Trash2 className="size-4" aria-hidden />
                          Delete permanently
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
            </CardContent>
          </Card>
        ) : (
          <EmptyState
            icon={<KeyRound aria-hidden />}
            title="No credentials"
            description="Add a registry token so pipeline runs can publish the SDKs they build."
            action={
              <Button onClick={() => setOpen(true)}>
                <Plus className="size-4" aria-hidden />
                Add credential
              </Button>
            }
          />
        )}
      </QueryBoundary>

      <AddCredentialDialog orgId={orgId} open={open} onOpenChange={setOpen} />

      <ConfirmDeleteDialog
        open={deleting !== null}
        onOpenChange={(next) => {
          if (!next) {
            setDeleting(null);
            setDeleteError(null);
          }
        }}
        title="Delete this credential?"
        // The registry stays out of it, said in prose rather than in a list: as one line
        // among others it read as boilerplate, and the reader who skims past it leaves a
        // live publish token in the wild.
        description="The record is removed for good. Revoking, which is reversible, only stops this application from using it. Deleting it here does not revoke the token at the registry — do that there too."
        confirmText={deleting?.label}
        consequences={[
          // What actually goes: the secret. Named as the thing it is, because "the
          // credential" is the row on screen and losing the row is not the cost — losing
          // the only copy of a token nobody can read back is.
          "The stored token, which no one can read back — publishing here again needs a new one",
          "Its label and registry address",
        ]}
        // The count is safe to read directly now: the dialog holds the last values it was
        // given for the length of its close animation, so `deleting` going null mid-exit no
        // longer flips this to the wrong branch.
        reassurances={[
          "Packages already published stay in their registries",
          ...(deleting?.usedBy.length === 0 ? ["No SDK target publishes with it"] : []),
        ]}
        details={
          deleting?.usedBy.length ? (
            <UsedByList
              orgId={orgId}
              usedBy={deleting.usedBy}
              label="Still publishing"
              note="Deletion is refused until each of these moves to another credential."
              onNavigate={() => setDeleting(null)}
            />
          ) : null
        }
        isPending={remove.isPending}
        error={deleteError}
        onConfirm={() =>
          deleting &&
          remove.mutate(deleting.id, {
            onSuccess: () => {
              toast.success("Credential deleted");
              setDeleting(null);
            },
            onError: (error) =>
              setDeleteError(
                error instanceof ApiError ? error.detail : "Could not delete the credential.",
              ),
          })
        }
      />

      {/* Not ConfirmDeleteDialog: nothing is deleted, and its Delete button and typed
          confirmation would both overstate an action Restore undoes. */}
      <Dialog
        open={revoking !== null}
        onOpenChange={(next) => {
          if (!next) setRevoking(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Revoke this credential?</DialogTitle>
            <DialogDescription>
              Publishing with this token stops immediately. Restore, in the same menu, puts it
              back — the token itself keeps working at the registry either way.
            </DialogDescription>
          </DialogHeader>

          {revoking && (
            <UsedByList
              orgId={orgId}
              usedBy={revoking.usedBy}
              label="Stops publishing"
              note="They start again if you restore the credential."
              onNavigate={() => setRevoking(null)}
            />
          )}

          <DialogFooter>
            <DialogClose render={<Button type="button" variant="outline">Cancel</Button>} />
            <Button onClick={() => revoking && revokeNow(revoking)} disabled={revoke.isPending}>
              <Ban className="size-4" aria-hidden />
              {revoke.isPending ? "Revoking…" : "Revoke"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/**
 * The targets a credential publishes for, named and linked rather than counted: "2 targets"
 * leaves someone hunting through projects for which ones, and the answer is one click away.
 *
 * Shared by the delete and revoke dialogs, which need the same list for opposite reasons —
 * one because the server refuses until it empties, the other because it is what breaks.
 */
function UsedByList({
  orgId,
  usedBy,
  label,
  note,
  onNavigate,
}: {
  orgId: string;
  usedBy: Credential["usedBy"];
  /**
   * The eyebrow, in the same voice as the "This removes" ledger above it. Short on purpose:
   * this treatment is tracked uppercase at 12px, and the sentence that used to sit here ran
   * to three lines of it. What that sentence explained is the `note`, in prose, under the
   * list it is about.
   */
  label: string;
  note: string;
  /** The links lead out of the dialog, which would otherwise still be open behind the page. */
  onNavigate: () => void;
}) {
  return (
    <div className="space-y-2 rounded-2xl border bg-muted/40 p-3.5">
      {/* Red like "This removes": both name why the action in front of you is blocked or
          costly, and a neutral label read as a heading rather than as a warning. */}
      <p className="text-xs font-medium tracking-wider text-destructive uppercase">{label}</p>
      <ItemGroup className="gap-1">
        {usedBy.map((use) => (
          <Item
            key={use.targetId}
            size="xs"
            render={
              <Link
                to={`/orgs/${orgId}/projects/${use.projectSlug}/targets`}
                onClick={onNavigate}
              />
            }
          >
            <ItemMedia variant="icon">
              <Package className="size-3.5 text-muted-foreground" aria-hidden />
            </ItemMedia>
            <ItemContent className="min-w-0">
              {/* `max-w-full` against `ItemTitle`'s own `w-fit`: with `truncate`'s nowrap,
                  fit-content resolves to the whole package name and the row would overflow
                  instead of ellipsising. */}
              <ItemTitle className="max-w-full truncate font-mono text-xs">
                {use.packageName}
              </ItemTitle>
              <ItemDescription className="truncate text-xs">
                {use.projectName} · {languageLabel[use.language]}
              </ItemDescription>
            </ItemContent>
            <ItemActions>
              <ArrowRight
                className="size-3.5 shrink-0 text-muted-foreground transition-transform group-hover/item:translate-x-0.5"
                aria-hidden
              />
            </ItemActions>
          </Item>
        ))}
      </ItemGroup>

      {/* Under the list, not above it: it explains what the rows mean, and reading it first
          asked the reader to hold a consequence in mind before seeing what it applies to. */}
      <p className="text-sm text-muted-foreground">{note}</p>
    </div>
  );
}

function AddCredentialDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [label, setLabel] = useState("");
  const [token, setToken] = useState("");
  // Prefilled rather than left blank: the public registry is what almost everyone wants, and
  // an empty field would make people wonder whether it is required.
  const [registryUrl, setRegistryUrl] = useState(NPM_REGISTRY);
  // Beside the field that caused them, not in a toast: a rejected token is something to look
  // at while fixing it, and a notification is gone by the time the value is read back.
  const [errors, setErrors] = useState<
    { label?: string; token?: string; registryUrl?: string; form?: string }>({});
  const create = useCreateCredential(orgId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setErrors({});

    create.mutate(
      { registry: RegistryKind.Npm, label, token, registryUrl },
      {
        onSuccess: () => {
          toast.success("Credential stored");
          setLabel("");
          setToken("");
          setRegistryUrl(NPM_REGISTRY);
          onOpenChange(false);
        },
        onError: (err) => {
          if (!(err instanceof ApiError)) {
            setErrors({ form: "Could not store the credential." });
            return;
          }

          const field = {
            label: err.fieldError("label"),
            token: err.fieldError("token"),
            registryUrl: err.fieldError("registryUrl"),
          };

          // A failure the API did not attribute to a field — a 409, a 500 — still has to be
          // said somewhere, or the dialog just sits there having apparently done nothing.
          setErrors(
            field.label || field.token || field.registryUrl ? field : { form: err.detail });
        },
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>Add an npm credential</DialogTitle>
            <DialogDescription>
              The token is encrypted immediately and only decrypted inside the publish step.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup className="py-4">
            <Field data-invalid={Boolean(errors.label)}>
              <FieldLabel htmlFor="label">Label</FieldLabel>
              <Input
                id="label"
                value={label}
                onChange={(e) => {
                  setLabel(e.target.value);
                  setErrors((current) => ({ ...current, label: undefined }));
                }}
                placeholder="npm automation token (acme)"
                aria-invalid={Boolean(errors.label)}
                aria-describedby={errors.label ? "label-error" : undefined}
                autoFocus
              />
              {/* The id is what `aria-describedby` above points at, so it stays on the error
                  itself: `FieldError` renders nothing at all while there is no message. */}
              <FieldError id="label-error">{errors.label}</FieldError>
            </Field>

            <Field data-invalid={Boolean(errors.token)}>
              <FieldLabel htmlFor="token">Token</FieldLabel>
              <Input
                id="token"
                type="password"
                value={token}
                onChange={(e) => {
                  // Cleared as it is retyped: leaving the field red while somebody corrects it
                  // says the new value is wrong too, which nothing has checked yet.
                  setToken(e.target.value);
                  setErrors((current) => ({ ...current, token: undefined }));
                }}
                placeholder="npm_…"
                aria-invalid={Boolean(errors.token)}
                aria-describedby={errors.token ? "token-error" : undefined}
                autoComplete="off"
              />
              <FieldError id="token-error">{errors.token}</FieldError>
            </Field>

            <Field data-invalid={Boolean(errors.registryUrl)}>
              <FieldLabel htmlFor="registry-url">Registry</FieldLabel>
              <Input
                id="registry-url"
                value={registryUrl}
                onChange={(e) => {
                  setRegistryUrl(e.target.value);
                  setErrors((current) => ({ ...current, registryUrl: undefined }));
                }}
                placeholder={NPM_REGISTRY}
                aria-invalid={Boolean(errors.registryUrl)}
                aria-describedby={errors.registryUrl ? "registry-url-error" : undefined}
                autoComplete="off"
              />
              {/* Still one or the other: the hint explains what the field is for, and once a
                  value has been rejected the reason it was rejected is the more useful line. */}
              {errors.registryUrl ? (
                <FieldError id="registry-url-error">{errors.registryUrl}</FieldError>
              ) : (
                <FieldDescription>
                  Where packages using this token are published. Point it at a private registry
                  — Verdaccio, Nexus, Artifactory — to keep them off the public one.
                </FieldDescription>
              )}
            </Field>

            {/* Announced like every other error in the app: silent, the only sign the save
                was refused is a red line a screen reader never reaches, and the dialog looks
                as though the click did nothing. */}
            {errors.form && (
              <p className="text-sm text-destructive" role="alert">
                {errors.form}
              </p>
            )}
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!label.trim() || !token.trim() || create.isPending}>
              {create.isPending ? "Saving…" : "Save credential"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
