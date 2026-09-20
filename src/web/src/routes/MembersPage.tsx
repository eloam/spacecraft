import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useOrganizationId } from "@/hooks/useRouteIdentity";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useQueryClient } from "@tanstack/react-query";
import { LogOut, MailX, MoreHorizontal, Plus, UserMinus, Users } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Field, FieldDescription, FieldError, FieldGroup, FieldLabel,
} from "@/components/ui/field";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import {
  Select, SelectContent, SelectItem, SelectTrigger,
} from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog, DialogClose, DialogContent, DialogDescription,
  DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { AccountAvatar } from "@/components/AccountAvatar";
import { timeAgo } from "@/lib/format";
import { ApiError, describeError } from "@/api/client";
import {
  keys, useChangeMemberRole, useInvitations, useInviteMember, useMembers, useRemoveMember,
  useRevokeInvitation, type Invitation, type Member,
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
  const { orgId: orgSlug = "" } = useParams();
  const orgId = useOrganizationId(orgSlug);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const members = useMembers(orgId);
  const changeRole = useChangeMemberRole(orgId);
  const remove = useRemoveMember(orgId);

  // Only an admin may see who has been asked to join, so only an admin asks. Read from the
  // list already on screen rather than fetched again: the row for you is in it.
  const yours = members.data?.find((member) => member.isYou);
  const invitations = useInvitations(orgId, (yours?.role ?? 0) >= Role.Admin);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [removeOpen, setRemoveOpen] = useState(false);
  // Kept apart from whether the dialog is open: the row has to outlive the close, or the
  // title fades out reading "Remove undefined?".
  const [removing, setRemoving] = useState<Member | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-medium tracking-tight">Members</h1>
          <p className="text-sm text-muted-foreground">
            Who can reach this organization's projects, credentials and packages.
          </p>
        </div>
        <Button onClick={() => setInviteOpen(true)}>
          <Plus className="size-4" aria-hidden />
          Invite someone
        </Button>
      </div>

      <QueryBoundary
        isLoading={members.isLoading}
        error={members.error}
        // A role change refetches this list; a dropped refresh should not replace the table
        // with an error when the rows on screen are still the ones the server last sent.
        hasData={members.data !== undefined}
        rows={3}
      >
        {members.data && members.data.length > 0 ? (
          // Framed like the credentials, specs and targets tables: one card, one banded header.
          <Card className="py-0">
            <CardContent className="p-0">
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
                  {members.data.map((member) => {
                    // The role being written to this row, if one is in flight. The cache
                    // still holds the old value until the refetch lands, so a select bound
                    // to it snapped visibly back to the previous role the moment a new one
                    // was picked. Per row, because one shared `isPending` greyed out every
                    // other select in the table while a single one was saving.
                    const savingRole =
                      changeRole.isPending && changeRole.variables?.userId === member.userId
                        ? changeRole.variables?.role
                        : undefined;
                    const shownRole = savingRole ?? member.role;

                    // A select here would only ever be refused. Nobody changes their own role
                    // — ask another owner or admin — and nobody changes the role of somebody
                    // who outranks them, or Admin would demote the Owner and delete the
                    // organization. Offering the control anyway promises something the server
                    // will not do, and the answer arrives as a toast after the fact.
                    const changeable = !member.isYou && (yours?.role ?? 0) >= member.role;

                    return (
                      <TableRow key={member.userId}>
                        <TableCell>
                          <div className="flex items-center gap-2">
                            <AccountAvatar displayName={member.displayName} email={member.email} />
                            {/* Capped as well as `min-w-0`: the cell is `whitespace-nowrap`, so
                                without a width to truncate against a long address just widened
                                the column and pushed the role and actions off screen. */}
                            <div className="min-w-0 max-w-[16rem]">
                              <div className="flex items-center gap-2">
                                <span className="min-w-0 truncate text-sm">
                                  {member.displayName || member.email}
                                </span>
                                {member.isYou && (
                                  <Badge variant="secondary" className="shrink-0">
                                    You
                                  </Badge>
                                )}
                              </div>
                              {member.displayName && (
                                <p className="truncate text-xs text-muted-foreground">
                                  {member.email}
                                </p>
                              )}
                            </div>
                          </div>
                        </TableCell>
                        <TableCell>
                          {!changeable ? (
                            <Badge variant="secondary">{roleLabel[shownRole]}</Badge>
                          ) : (
                          /* The app's Select, not a native one: a closed native select
                             commits on every arrow key, so passing Viewer → Owner wrote
                             three separate permission changes on the way through. */
                          <Select
                            value={shownRole}
                            disabled={savingRole !== undefined}
                            onValueChange={(value) => {
                              const role = Number(value);
                              if (role === member.role) return;
                              changeRole.mutate(
                                { userId: member.userId, role },
                                {
                                  onSuccess: () => toast.success("Role updated."),
                                  onError: (error) =>
                                    toast.error(describeError(error, "Could not change the role.")),
                                },
                              );
                            }}
                          >
                            <SelectTrigger
                              size="sm"
                              // A width, because the trigger is `w-fit` by default and the
                              // native select it replaces was not: a native one is as wide as
                              // its widest option whatever is chosen, so the column held still
                              // and every row lined up. Sized to its own label, "Viewer" and
                              // "Member" render as two different boxes in one column, and
                              // choosing a role resizes the box you just chose it in.
                              className="w-28"
                              aria-label={`Role for ${member.email}`}
                            >
                              {roleLabel[shownRole]}
                            </SelectTrigger>
                            <SelectContent>
                              {/* Up to your own and no further: granting a role you do not
                                  hold is the same refusal, one step later. */}
                              {Object.entries(roleLabel)
                                .filter(([value]) => Number(value) <= (yours?.role ?? 0))
                                .map(([value, label]) => (
                                  <SelectItem key={value} value={Number(value)}>
                                    {label}
                                  </SelectItem>
                                ))}
                            </SelectContent>
                          </Select>
                          )}
                        </TableCell>
                        <TableCell className="text-right text-xs text-muted-foreground">
                          {/* Absolute, unlike the columns scanned for recency: a join date
                              answers "since when". The exact moment is behind the hover, the
                              way every other timestamp in the app is. */}
                          <Tooltip>
                            <TooltipTrigger render={<span />}>
                              {new Date(member.joinedAt).toLocaleDateString()}
                            </TooltipTrigger>
                            <TooltipContent>
                              {new Date(member.joinedAt).toLocaleString()}
                            </TooltipContent>
                          </Tooltip>
                        </TableCell>
                        <TableCell className="text-right">
                          <DropdownMenu>
                            <DropdownMenuTrigger
                              render={<Button size="icon-sm" variant="ghost" />}
                              aria-label={
                                member.isYou
                                  ? "More actions for your membership"
                                  : `More actions for ${member.email}`
                              }
                            >
                              <MoreHorizontal className="size-4" aria-hidden />
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end" className="w-56">
                              {/* Named in words, and behind a confirmation: the same glyph
                                  used to mean "remove them" and "remove me" depending on the
                                  row, and told you which only in the toast afterwards. */}
                              <DropdownMenuItem
                                variant="destructive"
                                onClick={() => {
                                  setRemoveError(null);
                                  setRemoving(member);
                                  setRemoveOpen(true);
                                }}
                              >
                                {member.isYou ? (
                                  <LogOut className="size-4" aria-hidden />
                                ) : (
                                  <UserMinus className="size-4" aria-hidden />
                                )}
                                {member.isYou ? "Leave organization" : "Remove from organization"}
                              </DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : (
          <EmptyState
            icon={<Users aria-hidden />}
            title="No members yet"
            description="Invite an address. They join when they open the link, so the invitation reaches the person rather than whoever registered that address."
          />
        )}
      </QueryBoundary>

      {/* Below the members, and only when there are any: an organization with nothing
          outstanding should not carry an empty box explaining what one would look like. They
          are deliberately not rows in the table above — nobody here has joined anything. A
          list that failed to load is the one thing said here without any: kept quiet, it
          reads as nothing pending. */}
      {invitations.data && invitations.data.length > 0 && (
        <PendingInvitations orgId={orgId} invitations={invitations.data} />
      )}
      {/* Said the way the tables say it: with a list still up from an earlier load, the
          failure is a refresh that did not happen, not a list that is not there. */}
      {invitations.isError && (
        <p className="text-sm text-muted-foreground" role="status">
          {describeError(
            invitations.error,
            invitations.data
              ? "The pending invitations could not be refreshed."
              : "The pending invitations could not be loaded.",
          )}{" "}
          <button
            type="button"
            className="underline underline-offset-4"
            onClick={() => invitations.refetch()}
          >
            Try again
          </button>
        </p>
      )}

      <InviteDialog orgId={orgId} open={inviteOpen} onOpenChange={setInviteOpen} />

      <ConfirmDeleteDialog
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        onOpenChangeComplete={(open) => {
          if (!open) {
            setRemoving(null);
            setRemoveError(null);
          }
        }}
        title={
          removing?.isYou
            ? "Leave this organization?"
            : `Remove ${removing?.displayName || removing?.email}?`
        }
        description={
          removing?.isYou
            ? "You lose access straight away, and only an admin of this organization can add you back."
            : "They lose access straight away. You can add them back with the same email address."
        }
        // Typed only on your own row: adding someone back takes one form, whereas letting
        // yourself out is the one delete here you cannot undo without another person.
        confirmText={removing?.isYou ? removing.email : undefined}
        confirmLabel={removing?.isYou ? "Leave" : "Remove"}
        confirmIcon={
          removing?.isYou ? (
            <LogOut className="size-4" aria-hidden />
          ) : (
            <UserMinus className="size-4" aria-hidden />
          )
        }
        consequences={[
          removing?.isYou
            ? "Your access to every project, spec, credential and package here"
            : "Their access to every project, spec, credential and package here",
        ]}
        reassurances={[
          "Nothing this organization owns is deleted",
          removing?.isYou
            ? "Your account and any other organization you belong to"
            : "Their SpaceCraft account, and what they uploaded and published",
        ]}
        isPending={remove.isPending}
        error={removeError}
        onConfirm={() => {
          if (!removing) return;
          const left = removing.isYou;

          remove.mutate(removing.userId, {
            onSuccess: () => {
              toast.success(left ? "You left the organization." : "Member removed.");
              setRemoveOpen(false);
              if (left) {
                // Every query on this page answers 403 from here on, so go somewhere that
                // still loads. `exact`: the keys under this one belong to the organization
                // just left, and refetching them would only collect rejections on the way
                // out — but the list itself would otherwise still offer it for 30 seconds.
                queryClient.invalidateQueries({ queryKey: keys.organizations, exact: true });
                navigate("/");
              }
            },
            onError: (error) =>
              setRemoveError(describeError(error, "Could not remove the member.")),
          });
        }}
      />
    </div>
  );
}

/**
 * The invitations that have been sent and not yet used.
 *
 * They existed on the server from the day invitations did — listed and withdrawable through
 * the API — and were shown nowhere, so an admin who mistyped an address could only wait the
 * seven days out. What is on offer here is taking it back.
 */
function PendingInvitations({
  orgId,
  invitations,
}: {
  orgId: string;
  invitations: Invitation[];
}) {
  const revoke = useRevokeInvitation(orgId);

  return (
    <section className="space-y-3">
      <div>
        <h2 className="font-heading text-lg font-medium tracking-tight">Pending invitations</h2>
        <p className="text-sm text-muted-foreground">
          Sent and not yet accepted. Withdrawing one puts its link out immediately.
        </p>
      </div>

      <Card className="py-0">
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Address</TableHead>
                <TableHead>Role</TableHead>
                <TableHead className="text-right">Expires</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {invitations.map((invitation) => (
                <TableRow key={invitation.id}>
                  <TableCell>
                    <span className="text-sm">{invitation.email}</span>
                    <p className="text-xs text-muted-foreground">
                      Sent {timeAgo(invitation.createdAt)}
                    </p>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{roleLabel[invitation.role]}</Badge>
                  </TableCell>
                  <TableCell className="text-right text-sm text-muted-foreground">
                    {new Date(invitation.expiresAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell>
                    {/* No confirmation: nobody has joined anything, and re-inviting is the
                        same two clicks that sent this one. */}
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={`Withdraw the invitation to ${invitation.email}`}
                      disabled={revoke.isPending && revoke.variables === invitation.id}
                      onClick={() =>
                        revoke.mutate(invitation.id, {
                          onSuccess: () => toast.success(`Invitation to ${invitation.email} withdrawn.`),
                          onError: (error) =>
                            toast.error(describeError(error, "Could not withdraw the invitation.")),
                        })
                      }
                    >
                      <MailX className="size-4" aria-hidden />
                      Withdraw
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </section>
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
  const invite = useInviteMember(orgId);
  const form = useForm<z.input<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", role: Role.Member },
  });

  const selectedRole = Number(useWatch({ control: form.control, name: "role" }));

  const onSubmit = form.handleSubmit((values) =>
    invite.mutate(
      { email: values.email, role: Number(values.role) },
      {
        onSuccess: () => {
          // Sent, not added. Saying "member added" would describe something that has not
          // happened: the invitation is an offer, and the address has to answer it.
          toast.success(`Invitation sent to ${values.email}.`);
          form.reset();
          onOpenChange(false);
        },
        onError: (error: unknown) => {
          if (error instanceof ApiError && error.errors?.Email) {
            form.setError("email", { message: error.errors.Email.join(" ") });
            return;
          }
          toast.error(describeError(error, "Could not send the invitation."));
        },
      },
    ),
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Invite someone</DialogTitle>
            <DialogDescription>
              {/* The comment this replaces said there was no invitation flow yet, and that
                  the person had to have an account already. Both are now false, and the
                  second was the security problem: an address nobody had verified decided who
                  got the membership. */}
              A link goes to this address. They join when they open it, whether or not they
              have an account yet — and nothing changes until they do.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup className="py-2">
            <Field data-invalid={!!form.formState.errors.email}>
              <FieldLabel htmlFor="member-email">Email</FieldLabel>
              <Input
                id="member-email"
                type="email"
                autoComplete="off"
                aria-invalid={!!form.formState.errors.email}
                aria-describedby={form.formState.errors.email ? "member-email-error" : undefined}
                {...form.register("email")}
              />
              {/* The id is what `aria-describedby` above points at, so it stays on the error
                  itself: `FieldError` renders nothing at all while there is no message. */}
              <FieldError id="member-email-error" errors={[form.formState.errors.email]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="member-role">Role</FieldLabel>
              {/* Not registered like an input: Base UI's Select is not a native `<select>`,
                  so the form holds the value and the component reports the choice back. */}
              <Select
                value={selectedRole}
                onValueChange={(value) =>
                  form.setValue("role", Number(value), { shouldDirty: true })
                }
              >
                <SelectTrigger id="member-role" className="w-full">
                  {roleLabel[selectedRole]}
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(roleLabel).map(([value, label]) => (
                    <SelectItem key={value} value={Number(value)}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FieldDescription>{roleHelp[selectedRole]}</FieldDescription>
            </Field>
          </FieldGroup>

          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button">Cancel</Button>} />
            <Button type="submit" disabled={invite.isPending}>
              {invite.isPending ? "Sending…" : "Send invitation"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
