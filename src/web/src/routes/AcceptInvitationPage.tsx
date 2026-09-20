import { useState } from "react";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Building2, FolderGit2, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { AuthCard } from "@/components/AuthCard";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import {
  useAcceptInvitation,
  useDeclineInvitation,
  useInvitationPreview,
} from "@/api/queries";
import { roleLabel } from "@/api/types";
import { useAuthStore } from "@/stores/auth";
import { withReturnTo } from "@/lib/returnTo";
import { formatDate } from "@/lib/format";
import { ApiError, describeError } from "@/api/client";

/**
 * Where an invitation link lands.
 *
 * The token in the URL is the whole grant — it is what proves the person opening it can read
 * the mailbox the invitation was sent to, which is the one thing nothing else in this
 * application establishes.
 *
 * It used to spend that grant on arrival: opening the link joined the organization, and the
 * first thing anybody learned about it was that they were already in it. An invitation is an
 * offer. This page reads it out — who is asking, for what, alongside whom — and leaves the
 * answer to the person holding it.
 */
export function AcceptInvitationPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const accessToken = useAuthStore((state) => state.accessToken);
  const location = useLocation();
  const navigate = useNavigate();

  const preview = useInvitationPreview(accessToken ? token : "");
  const accept = useAcceptInvitation();
  const decline = useDeclineInvitation();

  const [declined, setDeclined] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const backToOrganizations = (
    <Link to="/" className="font-medium text-foreground underline-offset-4 hover:underline">
      Go to your organizations
    </Link>
  );

  if (!token) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard
          title="This link is incomplete"
          description="It carries no invitation."
          footer={backToOrganizations}
        >
          <p className="text-sm text-muted-foreground">
            Open the link from the invitation email exactly as it was sent — a link that was
            copied by hand often loses its last characters.
          </p>
        </AuthCard>
      </>
    );
  }

  // Signed out: the invitation says which organization, but joining still needs an account to
  // join it with. The token stays in the URL, so signing in comes back here.
  if (!accessToken) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard
          title="Sign in to see this invitation"
          description="An organization has invited you to SpaceCraft."
          footer={null}
        >
          <div className="space-y-3">
            <Button className="w-full" render={<Link to={withReturnTo("/login", location)} />}>
              Sign in
            </Button>
            <Button
              variant="outline"
              className="w-full"
              render={<Link to={withReturnTo("/register", location)} />}
            >
              Create an account
            </Button>
          </div>
        </AuthCard>
      </>
    );
  }

  if (declined) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard
          title="Invitation declined"
          description="The link no longer works, and nobody has been added to anything."
          footer={backToOrganizations}
        >
          <p className="text-sm text-muted-foreground">
            If you change your mind, whoever invited you can send another.
          </p>
        </AuthCard>
      </>
    );
  }

  // One answer for a token that never existed, one already used, one withdrawn and one that
  // lapsed — the API does not tell them apart, and neither does this. A 404 is the only
  // status the preview route declares, so it is the only one that is a verdict on the
  // invitation; anything else is the request failing, and gets another go rather than a
  // sentence about a link that may be fine.
  const unusable = preview.error instanceof ApiError && preview.error.status === 404;

  if (unusable || failure) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard
          title="This invitation cannot be used"
          description={
            failure ?? describeError(preview.error, "It may have been used, withdrawn, or expired.")
          }
          footer={backToOrganizations}
        >
          <p className="text-sm text-muted-foreground">
            Invitations work once and expire on their own, and sending a new one puts the
            previous link out. Ask whoever invited you to send another.
          </p>
        </AuthCard>
      </>
    );
  }

  if (preview.isError) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard
          title="The invitation could not be checked"
          description={describeError(preview.error, "The request failed. Try again in a moment.")}
          footer={backToOrganizations}
        >
          <Button className="w-full" onClick={() => preview.refetch()}>
            Try again
          </Button>
        </AuthCard>
      </>
    );
  }

  if (!preview.data) {
    return (
      <>
        <title>Invitation — SpaceCraft</title>

        <AuthCard title="Reading the invitation…" description="One moment." footer={null}>
          <div className="flex justify-center py-2">
            <Spinner className="size-5" />
          </div>
        </AuthCard>
      </>
    );
  }

  const invitation = preview.data;

  if (invitation.alreadyMember) {
    return (
      <>
        <title>{`Join ${invitation.organizationName} — SpaceCraft`}</title>

        <AuthCard
          title={`You are already in ${invitation.organizationName}`}
          description="Nothing to accept — this invitation is for an organization you have joined."
          footer={backToOrganizations}
        >
          <Button
            className="w-full"
            render={<Link to={`/orgs/${invitation.organizationSlug}`} />}
          >
            Open {invitation.organizationName}
          </Button>
        </AuthCard>
      </>
    );
  }

  const working = accept.isPending || decline.isPending;

  return (
    <>
      <title>{`Join ${invitation.organizationName} — SpaceCraft`}</title>

      <AuthCard
        title={`Join ${invitation.organizationName}`}
        description={`${invitation.invitedBy} invited you.`}
        footer={backToOrganizations}
      >
        <div className="space-y-4">
          <dl className="space-y-2.5 rounded-lg border p-3.5 text-sm">
            <div className="flex items-center justify-between gap-3">
              <dt className="flex items-center gap-2 text-muted-foreground">
                <Building2 className="size-4" aria-hidden />
                Your role
              </dt>
              <dd>
                <Badge variant="secondary">{roleLabel[invitation.role]}</Badge>
              </dd>
            </div>

            <div className="flex items-center justify-between gap-3">
              <dt className="flex items-center gap-2 text-muted-foreground">
                <Users className="size-4" aria-hidden />
                Members
              </dt>
              <dd>{invitation.memberCount}</dd>
            </div>

            {invitation.projects.length > 0 && (
              <div className="flex items-start justify-between gap-3">
                <dt className="flex items-center gap-2 text-muted-foreground">
                  <FolderGit2 className="size-4" aria-hidden />
                  Projects
                </dt>
                {/* Names, so that "is this the right Acme?" has an answer before the decision
                    rather than after it. */}
                <dd className="min-w-0 text-right">{invitation.projects.join(", ")}</dd>
              </div>
            )}
          </dl>

          <p className="text-sm text-muted-foreground">
            Accepting adds this account to the organization. The link works once, and expires on{" "}
            <time dateTime={invitation.expiresAt}>{formatDate(invitation.expiresAt)}</time>.
          </p>

          <div className="space-y-2">
            <Button
              className="w-full"
              disabled={working}
              onClick={() =>
                accept.mutate(token, {
                  onSuccess: () => navigate(`/orgs/${invitation.organizationSlug}`, { replace: true }),
                  onError: (error: unknown) =>
                    setFailure(
                      describeError(
                        error,
                        "The invitation could not be accepted. Ask for a new one.",
                      ),
                    ),
                })
              }
            >
              {accept.isPending ? "Joining…" : `Join ${invitation.organizationName}`}
            </Button>

            {/* Declining is not a way out of a mistake — it puts the link out. Said plainly
                under the button rather than behind a confirmation nobody reads. */}
            <Button
              variant="ghost"
              className="w-full"
              disabled={working}
              onClick={() =>
                decline.mutate(token, {
                  onSuccess: () => setDeclined(true),
                  onError: (error: unknown) =>
                    setFailure(describeError(error, "The invitation could not be declined.")),
                })
              }
            >
              {decline.isPending ? "Declining…" : "Decline"}
            </Button>
          </div>
        </div>
      </AuthCard>
    </>
  );
}
