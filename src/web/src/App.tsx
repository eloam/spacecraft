// Route params named orgId/projectId carry the *slug*, not the id: /orgs/acme-corp
// reads better than a GUID and is what people paste to each other. The API accepts
// either form, so links minted before this change still resolve.
import { Suspense, lazy, type ReactNode } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuthStore } from "@/stores/auth";
import { useReturnPath } from "@/hooks/useReturnPath";
import { withReturnTo } from "@/lib/returnTo";
import { AppShell } from "@/components/layout/AppShell";
import { PageSpinner } from "@/components/PageSpinner";

/**
 * Every page is its own chunk, fetched the first time its route is visited: the sign-in page
 * used to download the whole application, spec viewer and all, before it could draw a form.
 * The router navigates inside a transition, so moving between pages keeps the current one on
 * screen while the next chunk arrives; the fallbacks below show only on a cold load.
 */
const LoginPage = lazy(() =>
  import("@/routes/LoginPage").then((m) => ({ default: m.LoginPage })),
);
const RegisterPage = lazy(() =>
  import("@/routes/RegisterPage").then((m) => ({ default: m.RegisterPage })),
);
const AccountLayout = lazy(() =>
  import("@/routes/account/AccountLayout").then((m) => ({ default: m.AccountLayout })),
);
const AccountProfilePage = lazy(() =>
  import("@/routes/account/AccountProfilePage").then((m) => ({ default: m.AccountProfilePage })),
);
const AccountSecurityPage = lazy(() =>
  import("@/routes/account/AccountSecurityPage").then((m) => ({ default: m.AccountSecurityPage })),
);
const AccountDangerPage = lazy(() =>
  import("@/routes/account/AccountDangerPage").then((m) => ({ default: m.AccountDangerPage })),
);
const AccountNotificationsPage = lazy(() =>
  import("@/routes/account/AccountNotificationsPage").then((m) => ({ default: m.AccountNotificationsPage })),
);
const PublicationsPage = lazy(() =>
  import("@/routes/PublicationsPage").then((m) => ({ default: m.PublicationsPage })),
);
const MembersPage = lazy(() =>
  import("@/routes/MembersPage").then((m) => ({ default: m.MembersPage })),
);
const OrganizationSettingsPage = lazy(() =>
  import("@/routes/OrganizationSettingsPage").then((m) => ({ default: m.OrganizationSettingsPage })),
);
const ProjectSettingsPage = lazy(() =>
  import("@/routes/project/ProjectSettingsPage").then((m) => ({ default: m.ProjectSettingsPage })),
);
const ForgotPasswordPage = lazy(() =>
  import("@/routes/ForgotPasswordPage").then((m) => ({ default: m.ForgotPasswordPage })),
);
const ResetPasswordPage = lazy(() =>
  import("@/routes/ResetPasswordPage").then((m) => ({ default: m.ResetPasswordPage })),
);
const AcceptInvitationPage = lazy(() =>
  import("@/routes/AcceptInvitationPage").then((m) => ({ default: m.AcceptInvitationPage })),
);
const OrganizationsPage = lazy(() =>
  import("@/routes/OrganizationsPage").then((m) => ({ default: m.OrganizationsPage })),
);
const ProjectsPage = lazy(() =>
  import("@/routes/ProjectsPage").then((m) => ({ default: m.ProjectsPage })),
);
const ProjectLayout = lazy(() =>
  import("@/routes/project/ProjectLayout").then((m) => ({ default: m.ProjectLayout })),
);
const ProjectSpecsPage = lazy(() =>
  import("@/routes/project/ProjectSpecsPage").then((m) => ({ default: m.ProjectSpecsPage })),
);
const ProjectTargetsPage = lazy(() =>
  import("@/routes/project/ProjectTargetsPage").then((m) => ({ default: m.ProjectTargetsPage })),
);
const OrganizationRunsPage = lazy(() =>
  import("@/routes/OrganizationRunsPage").then((m) => ({ default: m.OrganizationRunsPage })),
);
const RunPage = lazy(() =>
  import("@/routes/RunPage").then((m) => ({ default: m.RunPage })),
);
const CredentialsPage = lazy(() =>
  import("@/routes/CredentialsPage").then((m) => ({ default: m.CredentialsPage })),
);
const NotFoundPage = lazy(() =>
  import("@/routes/NotFoundPage").then((m) => ({ default: m.NotFoundPage })),
);

function RequireAuth({ children }: { children: ReactNode }) {
  const accessToken = useAuthStore((s) => s.accessToken);
  const location = useLocation();
  // Where they were going rides along to the sign-in page, so following a link into the
  // application while signed out costs a sign-in, not the link.
  return accessToken ? <>{children}</> : <Navigate to={withReturnTo("/login", location)} replace />;
}

/**
 * Signed in — which for these two routes covers both arriving with a session and gaining one
 * a moment ago, since signing in only changes the store and lets the router re-render.
 *
 * Sending everyone to the dashboard is what dropped an invitation on the floor: the link was
 * opened, sign-in was offered, it worked, and the token in the URL was never seen again.
 */
function ContinueOn() {
  return <Navigate to={useReturnPath()} replace />;
}

export default function App() {
  const accessToken = useAuthStore((s) => s.accessToken);

  return (
    // Around the routes outside the shell; the shell holds its own round its outlet, so its
    // bar stays on screen while a page's chunk loads.
    <Suspense fallback={<PageSpinner />}>
      <Routes>
        <Route path="/login" element={accessToken ? <ContinueOn /> : <LoginPage />} />
        <Route path="/register" element={accessToken ? <ContinueOn /> : <RegisterPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        {/* Reachable while signed in too: a reset link is often opened on a device that
            still has a session, and bouncing to the dashboard would strand the user. */}
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        {/* Outside the authenticated shell: the link arrives in a mailbox, and whoever opens it
            may not be signed in — or may not have an account yet. The page handles both. */}
        <Route path="/invitations/accept" element={<AcceptInvitationPage />} />

        <Route
          element={
            <RequireAuth>
              <AppShell />
            </RequireAuth>
          }
        >
          <Route path="/" element={<OrganizationsPage />} />
          <Route path="/account" element={<AccountLayout />}>
            <Route index element={<AccountProfilePage />} />
            <Route path="security" element={<AccountSecurityPage />} />
            <Route path="notifications" element={<AccountNotificationsPage />} />
            <Route path="danger" element={<AccountDangerPage />} />
          </Route>
          <Route path="/orgs/:orgId" element={<ProjectsPage />} />
          <Route path="/orgs/:orgId/credentials" element={<CredentialsPage />} />
          <Route path="/orgs/:orgId/publications" element={<PublicationsPage />} />
          <Route path="/orgs/:orgId/members" element={<MembersPage />} />
          <Route path="/orgs/:orgId/settings" element={<OrganizationSettingsPage />} />
          <Route path="/orgs/:orgId/projects/:projectId" element={<ProjectLayout />}>
            <Route index element={<ProjectSpecsPage />} />
            <Route path="targets" element={<ProjectTargetsPage />} />
            <Route path="settings" element={<ProjectSettingsPage />} />
          </Route>
          <Route path="/orgs/:orgId/runs" element={<OrganizationRunsPage />} />
          <Route path="/orgs/:orgId/runs/:runId" element={<RunPage />} />
          {/* Inside the shell, so an unknown address is said in the application rather than
              swallowed by a redirect to the dashboard — and behind RequireAuth, so a signed-out
              visitor learns nothing about which addresses exist. */}
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </Suspense>
  );
}
