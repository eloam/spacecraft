// Route params named orgId/projectId carry the *slug*, not the id: /orgs/acme-corp
// reads better than a GUID and is what people paste to each other. The API accepts
// either form, so links minted before this change still resolve.
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuthStore } from "@/stores/auth";
import { useReturnPath, withReturnTo } from "@/lib/returnTo";
import { AppShell } from "@/components/layout/AppShell";
import { LoginPage } from "@/routes/LoginPage";
import { RegisterPage } from "@/routes/RegisterPage";
import { AccountLayout } from "@/routes/account/AccountLayout";
import { AccountProfilePage } from "@/routes/account/AccountProfilePage";
import { AccountSecurityPage } from "@/routes/account/AccountSecurityPage";
import { AccountDangerPage } from "@/routes/account/AccountDangerPage";
import { AccountNotificationsPage } from "@/routes/account/AccountNotificationsPage";
import { PublicationsPage } from "@/routes/PublicationsPage";
import { MembersPage } from "@/routes/MembersPage";
import { OrganizationSettingsPage } from "@/routes/OrganizationSettingsPage";
import { ProjectSettingsPage } from "@/routes/ProjectSettingsPage";
import { ForgotPasswordPage } from "@/routes/ForgotPasswordPage";
import { ResetPasswordPage } from "@/routes/ResetPasswordPage";
import { AcceptInvitationPage } from "@/routes/AcceptInvitationPage";
import { OrganizationsPage } from "@/routes/OrganizationsPage";
import { ProjectsPage } from "@/routes/ProjectsPage";
import { ProjectLayout } from "@/routes/project/ProjectLayout";
import { ProjectSpecsPage } from "@/routes/project/ProjectSpecsPage";
import { ProjectTargetsPage } from "@/routes/project/ProjectTargetsPage";
import { OrganizationRunsPage } from "@/routes/OrganizationRunsPage";
import { RunPage } from "@/routes/RunPage";
import { CredentialsPage } from "@/routes/CredentialsPage";
import type { ReactNode } from "react";

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
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
