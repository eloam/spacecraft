import { useState } from "react";
import { useParams } from "react-router-dom";
import { Ban, KeyRound, MoreHorizontal, Plus, RotateCcw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import {
  Dialog,
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
import { RegistryKind, registryLabel } from "@/api/types";
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
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
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

      <QueryBoundary isLoading={isLoading} error={error}>
        {data?.length ? (
          // Framed like the specs and targets tables: one card, one banded header.
          <Card className="py-0">
            <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Kind</TableHead>
                <TableHead>Label</TableHead>
                <TableHead>Publishes to</TableHead>
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
                      npm credentials in one organization can point at different registries. */}
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {credential.registryUrl}
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    ••••{credential.lastFour}
                  </TableCell>
                  <TableCell>
                    <Badge variant={credential.isRevoked ? "destructive" : "outline"}>
                      {credential.isRevoked ? "Revoked" : "Active"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {credential.lastUsedAt ? new Date(credential.lastUsedAt).toLocaleString() : "—"}
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
                            onClick={() =>
                              revoke.mutate(credential.id, {
                                onSuccess: () => toast.success("Credential revoked"),
                                onError: () => toast.error("Could not revoke the credential."),
                              })
                            }
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
            icon={<KeyRound className="size-8" />}
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
        description="The record is removed for good. Revoking, which is reversible, only stops this application from using it."
        confirmText={deleting?.label}
        consequences={[
          "The token itself is not revoked at the registry — do that there too",
          deleting?.targetsUsing
            ? `${deleting.targetsUsing} SDK target${deleting.targetsUsing === 1 ? " publishes" : "s publish"} with it — deleting will be refused`
            : "No SDK target publishes with it",
        ]}
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

          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="label">Label</Label>
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
              {errors.label && (
                <p id="label-error" className="text-sm text-destructive">{errors.label}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="token">Token</Label>
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
              {errors.token && (
                <p id="token-error" className="text-sm text-destructive">{errors.token}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="registry-url">Registry</Label>
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
              {errors.registryUrl ? (
                <p id="registry-url-error" className="text-sm text-destructive">
                  {errors.registryUrl}
                </p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  Where packages using this token are published. Point it at a private registry
                  — Verdaccio, Nexus, Artifactory — to keep them off the public one.
                </p>
              )}
            </div>

            {errors.form && <p className="text-sm text-destructive">{errors.form}</p>}
          </div>

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
