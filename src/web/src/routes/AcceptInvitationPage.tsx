import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { AuthCard } from "@/components/AuthCard";
import { Spinner } from "@/components/ui/spinner";
import { useAcceptInvitation } from "@/api/queries";
import { useAuthStore } from "@/stores/auth";
import { withReturnTo } from "@/lib/returnTo";
import { ApiError } from "@/api/client";

/**
 * Where an invitation link lands.
 *
 * The token in the URL is the whole grant — it is what proves the person opening it can read
 * the mailbox the invitation was sent to, which is the one thing nothing else in this
 * application establishes. The page therefore does one thing and says plainly how it went.
 */
export function AcceptInvitationPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const accessToken = useAuthStore((state) => state.accessToken);
  const accept = useAcceptInvitation();
  const navigate = useNavigate();
  const location = useLocation();
  const [failure, setFailure] = useState<string | null>(null);

  const backToOrganizations = (
    <Link to="/" className="font-medium text-foreground underline-offset-4 hover:underline">
      Go to your organizations
    </Link>
  );

  // Accepting is a write, and React runs effects twice in development. Without this the
  // second run spends the invitation the first one already accepted, and the page reports
  // the failure rather than the success that actually happened.
  const attempted = useRef(false);

  useEffect(() => {
    if (!token || !accessToken || attempted.current) return;

    attempted.current = true;

    accept.mutate(token, {
      onSuccess: () => navigate("/", { replace: true }),
      onError: (error: unknown) =>
        setFailure(
          error instanceof ApiError
            ? error.detail
            : "The invitation could not be accepted. Ask for a new one.",
        ),
    });
  }, [token, accessToken, accept, navigate]);

  if (!token) {
    return (
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
    );
  }

  // Signed out: the invitation says which organization, but joining still needs an account to
  // join it with. The token stays in the URL, so signing in comes back here.
  if (!accessToken) {
    return (
      <AuthCard
        title="Sign in to accept"
        description="An organization has invited you to SpaceCraft."
        footer={null}
      >
        <div className="space-y-3">
          <Button
            className="w-full"
            render={<Link to={withReturnTo("/login", location)} />}
          >
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
    );
  }

  if (failure) {
    return (
      <AuthCard
        title="This invitation cannot be used"
        description={failure}
        footer={backToOrganizations}
      >
        <p className="text-sm text-muted-foreground">
          Invitations work once and expire on their own, and sending a new one puts the
          previous link out. Ask whoever invited you to send another.
        </p>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Joining…" description="Adding you to the organization." footer={null}>
      <div className="flex justify-center py-2">
        <Spinner className="size-5" />
      </div>
    </AuthCard>
  );
}
