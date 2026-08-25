import { useState } from "react";
import { useParams } from "react-router-dom";
import { KeyRound, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
import { useCreateCredential, useCredentials, useRevokeCredential } from "@/api/queries";
import { RegistryKind, registryLabel } from "@/api/types";
import { ApiError } from "@/api/client";
import { toast } from "sonner";

export function CredentialsPage() {
  const { orgId = "" } = useParams();
  const { data, isLoading, error } = useCredentials(orgId);
  const revoke = useRevokeCredential(orgId);
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Registry credentials</h1>
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
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Registry</TableHead>
                <TableHead>Label</TableHead>
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
                    {!credential.isRevoked && (
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Revoke ${credential.label}`}
                        onClick={() =>
                          revoke.mutate(credential.id, {
                            onSuccess: () => toast.success("Credential revoked"),
                            onError: () => toast.error("Could not revoke the credential."),
                          })
                        }
                      >
                        <Trash2 className="size-4" aria-hidden />
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
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
  const create = useCreateCredential(orgId);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    create.mutate(
      { registry: RegistryKind.Npm, label, token },
      {
        onSuccess: () => {
          toast.success("Credential stored");
          setLabel("");
          setToken("");
          onOpenChange(false);
        },
        onError: (err) =>
          toast.error(err instanceof ApiError ? err.detail : "Could not store the credential."),
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
                onChange={(e) => setLabel(e.target.value)}
                placeholder="npm automation token (acme)"
                autoFocus
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="token">Token</Label>
              <Input
                id="token"
                type="password"
                value={token}
                onChange={(e) => setToken(e.target.value)}
                placeholder="npm_…"
                autoComplete="off"
              />
            </div>
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
