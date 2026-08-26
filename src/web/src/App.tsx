import { Navigate, Route, Routes } from "react-router-dom";
import { useAuthStore } from "@/stores/auth";
import { AppShell } from "@/components/layout/AppShell";
import { LoginPage } from "@/routes/LoginPage";
import { RegisterPage } from "@/routes/RegisterPage";
import { AccountPage } from "@/routes/AccountPage";
import { ForgotPasswordPage } from "@/routes/ForgotPasswordPage";
import { ResetPasswordPage } from "@/routes/ResetPasswordPage";
import { OrganizationsPage } from "@/routes/OrganizationsPage";
import { ProjectsPage } from "@/routes/ProjectsPage";
import { ProjectPage } from "@/routes/ProjectPage";
import { RunPage } from "@/routes/RunPage";
import { CredentialsPage } from "@/routes/CredentialsPage";
import type { ReactNode } from "react";

function RequireAuth({ children }: { children: ReactNode }) {
  const accessToken = useAuthStore((s) => s.accessToken);
  return accessToken ? <>{children}</> : <Navigate to="/login" replace />;
}

export default function App() {
  const accessToken = useAuthStore((s) => s.accessToken);

  return (
    <Routes>
      <Route path="/login" element={accessToken ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route path="/register" element={accessToken ? <Navigate to="/" replace /> : <RegisterPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      {/* Reachable while signed in too: a reset link is often opened on a device that
          still has a session, and bouncing to the dashboard would strand the user. */}
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      <Route
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route path="/" element={<OrganizationsPage />} />
        <Route path="/account" element={<AccountPage />} />
        <Route path="/orgs/:orgId" element={<ProjectsPage />} />
        <Route path="/orgs/:orgId/credentials" element={<CredentialsPage />} />
        <Route path="/orgs/:orgId/projects/:projectId" element={<ProjectPage />} />
        <Route path="/orgs/:orgId/runs/:runId" element={<RunPage />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
