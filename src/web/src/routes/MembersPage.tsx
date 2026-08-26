import { useState } from "react";
import { useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Plus, UserMinus, Users } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { AccountAvatar } from "@/components/AccountAvatar";
import { ApiError } from "@/api/client";
import {
  useAddMember, useChangeMemberRole, useMembers, useRemoveMember,
} from "@/api/queries";

/** Mirrors SpaceCraft.Domain.Organizations.OrganizationRole. */
const Role = { Viewer: 1, Member: 2, Admin: 3, Owner: 4 } as const;

const roleLabel: Record<number, string> = {
  [Role.Viewer]: "Viewer",
  [Role.Member]: "Member",
  [Role.Admin]: "Admin",
  [Role.Owner]: "Owner",
};

const roleHelp: Record<number, string> = {
  [Role.Viewer]: "Can look at projects, specs and runs.",
  [Role.Member]: "Can upload specs and generate SDKs.",
  [Role.Admin]: "Can also publish, manage credentials and add people.",
  [Role.Owner]: "Full control, including deleting the organization.",
};

const schema = z.object({
  email: z.string().email("Enter a valid email address."),
  role: z.coerce.number(),
});

export function MembersPage() {
  const { orgId = "" } = useParams();
  const members = useMembers(orgId);
  const changeRole = useChangeMemberRole(orgId);
  const remove = useRemoveMember(orgId);
  const [inviteOpen, setInviteOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-sm text-muted-foreground">
            Who can reach this organization's projects, credentials and packages.
          </p>
        </div>
        <Button onClick={() => setInviteOpen(true)}>
          <Plus className="size-4" aria-hidden />
          Add member
        </Button>
      </div>

      <QueryBoundary isLoading={members.isLoading} error={members.error} rows={3}>
        {members.data && members.data.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Person</TableHead>
                <TableHead>Role</TableHead>
                <TableHead className="text-right">Joined</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {members.data.map((member) => (
                <TableRow key={member.userId}>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <AccountAvatar displayName={member.displayName} email={member.email} />
                      <div className="min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="truncate text-sm">
                            {member.displayName || member.email}
                          </span>
                          {member.isYou && <Badge variant="secondary">You</Badge>}
                        </div>
                        {member.displayName && (
                          <p className="truncate text-xs text-muted-foreground">{member.email}</p>
                        )}
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    <select
                      aria-label={`Role for ${member.email}`}
                      value={member.role}
                      onChange={(e) =>
                        changeRole.mutate(
                          { userId: member.userId, role: Number(e.target.value) },
                          {
                            onSuccess: () => toast.success("Role updated."),
                            onError: (error) =>
                              toast.error(
                                error instanceof ApiError ? error.detail : "Could not change the role.",
                              ),
                          },
                        )
                      }
                      className="h-8 rounded-lg border bg-background px-2 text-sm"
                    >
                      {Object.entries(roleLabel).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </TableCell>
                  <TableCell className="text-right text-xs text-muted-foreground">
                    {new Date(member.joinedAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() =>
                        remove.mutate(member.userId, {
                          onSuccess: () =>
                            toast.success(member.isYou ? "You left the organization." : "Member removed."),
                          onError: (error) =>
                            toast.error(
                              error instanceof ApiError ? error.detail : "Could not remove the member.",
                            ),
                        })
                      }
                      disabled={remove.isPending}
                    >
                      <UserMinus className="size-4" aria-hidden />
                      <span className="sr-only">
                        {member.isYou ? "Leave organization" : `Remove ${member.email}`}
                      </span>
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <EmptyState
            icon={<Users className="size-6" aria-hidden />}
            title="No members yet"
            description="Add someone with a SpaceCraft account to work on these projects together."
          />
        )}
      </QueryBoundary>

      <InviteDialog orgId={orgId} open={inviteOpen} onOpenChange={setInviteOpen} />
    </div>
  );
}

function InviteDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const add = useAddMember(orgId);
  const form = useForm<z.input<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", role: Role.Member },
  });

  const selectedRole = Number(form.watch("role"));

  const onSubmit = form.handleSubmit((values) =>
    add.mutate(
      { email: values.email, role: Number(values.role) },
      {
        onSuccess: () => {
          toast.success("Member added.");
          form.reset();
          onOpenChange(false);
        },
        onError: (error) => {
          if (error instanceof ApiError && error.errors?.Email) {
            form.setError("email", { message: error.errors.Email.join(" ") });
            return;
          }
          toast.error(error instanceof ApiError ? error.detail : "Could not add the member.");
        },
      },
    ),
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Add a member</DialogTitle>
            <DialogDescription>
              {/* There is no invitation flow yet: the person must already have an account,
                  and saying so upfront avoids a dead end after the form is filled in. */}
              They need a SpaceCraft account already — adding them takes effect immediately.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div className="space-y-2">
              <Label htmlFor="member-email">Email</Label>
              <Input id="member-email" type="email" autoComplete="off" {...form.register("email")} />
              {form.formState.errors.email && (
                <p className="text-sm text-destructive">{form.formState.errors.email.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="member-role">Role</Label>
              <select
                id="member-role"
                {...form.register("role")}
                className="h-9 w-full rounded-lg border bg-background px-3 text-sm"
              >
                {Object.entries(roleLabel).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
              <p className="text-xs text-muted-foreground">{roleHelp[selectedRole]}</p>
            </div>
          </div>

          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button">Cancel</Button>} />
            <Button type="submit" disabled={add.isPending}>
              {add.isPending ? "Adding…" : "Add member"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
