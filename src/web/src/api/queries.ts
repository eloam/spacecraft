import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from "@tanstack/react-query";
import { api } from "./client";
import { isTerminal } from "./types";
import type {
  AuthResponse,
  Credential,
  Organization,
  Project,
  Run,
  SdkTarget,
  SpecUpload,
  SpecVersion,
  User,
} from "./types";
import { useAuthStore } from "@/stores/auth";

/**
 * Query keys are built from this one place so an invalidation can never miss a cache entry
 * because of a typo in a hand-written key array.
 */
export const keys = {
  me: ["me"] as const,
  organizations: ["organizations"] as const,
  organization: (orgId: string) => ["organizations", orgId] as const,
  projects: (orgId: string) => ["organizations", orgId, "projects"] as const,
  project: (orgId: string, projectId: string) => ["organizations", orgId, "projects", projectId] as const,
  specs: (orgId: string, projectId: string) =>
    ["organizations", orgId, "projects", projectId, "specs"] as const,
  runs: (orgId: string, projectId?: string) =>
    ["organizations", orgId, "runs", projectId ?? "all"] as const,
  run: (orgId: string, runId: string) => ["organizations", orgId, "runs", runId] as const,
  stepLog: (orgId: string, runId: string, step: string) =>
    ["organizations", orgId, "runs", runId, "log", step] as const,
  credentials: (orgId: string) => ["organizations", orgId, "credentials"] as const,
};

// --- auth -----------------------------------------------------------------------------

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { email: string; password: string }) =>
      api.post<AuthResponse>("/auth/login", input),
    onSuccess: (data) => {
      setSession(data.accessToken, data.refreshToken, data.user);
      queryClient.clear();
    },
  });
}

export type Account = {
  id: string;
  email: string;
  displayName: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  organizationCount: number;
};

export type Session = {
  id: string;
  startedAt: string;
  lastUsedAt: string;
  expiresAt: string;
  isCurrent: boolean;
};

export type DeletionBlocker = { organizationName: string; reason: string };

export type Publication = {
  id: string;
  pipelineRunId: string;
  apiProjectId: string | null;
  projectName: string | null;
  registry: number;
  packageName: string;
  version: string;
  status: number;
  registryUrl: string | null;
  failureReason: string | null;
  createdAt: string;
  completedAt: string | null;
};

export type Member = {
  userId: string;
  email: string;
  displayName: string | null;
  role: number;
  isYou: boolean;
  joinedAt: string;
};

export type SpecDiagnostic = {
  severity: number;
  message: string;
  pointer: string | null;
};

export function usePublications(orgId: string, projectId?: string) {
  return useQuery({
    queryKey: ["publications", orgId, projectId ?? "all"],
    queryFn: () =>
      api.get<Publication[]>(
        `/organizations/${orgId}/publications${projectId ? `?projectId=${projectId}` : ""}`,
      ),
    enabled: Boolean(orgId),
  });
}

export function useSpecDiagnostics(orgId: string, projectId: string, specId: string | null) {
  return useQuery({
    queryKey: ["diagnostics", orgId, projectId, specId],
    queryFn: () =>
      api.get<SpecDiagnostic[]>(
        `/organizations/${orgId}/projects/${projectId}/specs/${specId}/diagnostics`,
      ),
    // Only fetched when a spec is actually open: 413 findings is not a payload to prefetch.
    enabled: Boolean(orgId && projectId && specId),
  });
}

export function useMembers(orgId: string) {
  return useQuery({
    queryKey: ["members", orgId],
    queryFn: () => api.get<Member[]>(`/organizations/${orgId}/members`),
    enabled: Boolean(orgId),
  });
}

export function useAddMember(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { email: string; role: number }) =>
      api.post<Member>(`/organizations/${orgId}/members`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["members", orgId] }),
  });
}

export function useChangeMemberRole(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { userId: string; role: number }) =>
      api.patch<Member>(`/organizations/${orgId}/members/${input.userId}`, { role: input.role }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["members", orgId] }),
  });
}

export function useRemoveMember(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => api.delete<void>(`/organizations/${orgId}/members/${userId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["members", orgId] }),
  });
}

export function useDeleteOrganization() {
  const queryClient = useQueryClient();
  return useMutation({
    // The name is typed back as confirmation; the server checks it too, so a client that
    // skipped the dialog gets the same answer.
    mutationFn: (input: { orgId: string; name: string }) =>
      api.delete<void>(`/organizations/${input.orgId}`, { name: input.name }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.organizations }),
  });
}

export function useDeleteProject(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (projectId: string) =>
      api.delete<void>(`/organizations/${orgId}/projects/${projectId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.projects(orgId) }),
  });
}

export function useDeleteTarget(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (targetId: string) =>
      api.delete<void>(`/organizations/${orgId}/projects/${projectId}/targets/${targetId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) }),
  });
}

export function useAccount() {
  return useQuery({
    queryKey: ["account"],
    queryFn: () => api.get<Account>("/account"),
  });
}

export function useUpdateProfile() {
  const queryClient = useQueryClient();
  const setSession = useAuthStore((s) => s.setSession);
  const accessToken = useAuthStore((s) => s.accessToken);
  const refreshToken = useAuthStore((s) => s.refreshToken);

  return useMutation({
    mutationFn: (input: { displayName: string | null }) =>
      api.patch<Account>("/account", input),
    onSuccess: (account) => {
      queryClient.setQueryData(["account"], account);
      // The header reads the name from the auth store, so it has to hear about this too.
      if (accessToken && refreshToken) {
        setSession(accessToken, refreshToken, {
          id: account.id,
          email: account.email,
          displayName: account.displayName,
        });
      }
    },
  });
}

export function useChangePassword() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      api.post<void>("/account/password", input),
    // Changing the password revokes other sessions, so the list is now stale.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["sessions"] }),
  });
}

export function useSessions() {
  return useQuery({
    queryKey: ["sessions"],
    queryFn: () => api.get<Session[]>("/account/sessions"),
  });
}

export function useRevokeSession() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (sessionId: string) => api.delete<void>(`/account/sessions/${sessionId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["sessions"] }),
  });
}

export function useRevokeOtherSessions() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => api.delete<void>("/account/sessions"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["sessions"] }),
  });
}

export function useDeletionBlockers() {
  return useQuery({
    queryKey: ["deletion-blockers"],
    queryFn: () => api.get<DeletionBlocker[]>("/account/deletion-blockers"),
  });
}

export function useDeleteAccount() {
  const clear = useAuthStore((s) => s.clear);

  return useMutation({
    mutationFn: (input: { password: string }) =>
      api.delete<void>("/account", input),
    onSuccess: () => clear(),
  });
}

export function useForgotPassword() {
  return useMutation({
    mutationFn: (input: { email: string }) =>
      api.post<{ message: string }>("/auth/forgot-password", input),
  });
}

export function useResetPassword() {
  return useMutation({
    mutationFn: (input: { token: string; newPassword: string }) =>
      api.post<{ message: string }>("/auth/reset-password", input),
  });
}

export function useRegister() {
  const setSession = useAuthStore((s) => s.setSession);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { email: string; password: string; displayName?: string }) =>
      api.post<AuthResponse>("/auth/register", input),
    onSuccess: (data) => {
      setSession(data.accessToken, data.refreshToken, data.user);
      queryClient.clear();
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { refreshToken } = useAuthStore.getState();
      if (refreshToken) {
        // Best effort: a failed revoke must not trap the user in a session they left.
        await api.post("/auth/logout", { refreshToken }).catch(() => undefined);
      }
    },
    onSettled: () => {
      useAuthStore.getState().clear();
      queryClient.clear();
    },
  });
}

export function useMe(enabled: boolean) {
  return useQuery({
    queryKey: keys.me,
    queryFn: () => api.get<User>("/auth/me"),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

// --- organizations --------------------------------------------------------------------

export function useOrganizations() {
  return useQuery({
    queryKey: keys.organizations,
    queryFn: () => api.get<Organization[]>("/organizations"),
  });
}

export function useOrganization(orgId: string) {
  return useQuery({
    queryKey: keys.organization(orgId),
    queryFn: () => api.get<Organization>(`/organizations/${orgId}`),
    enabled: Boolean(orgId),
  });
}

export function useCreateOrganization() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; slug?: string }) =>
      api.post<Organization>("/organizations", input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.organizations }),
  });
}

// --- projects -------------------------------------------------------------------------

export function useProjects(orgId: string) {
  return useQuery({
    queryKey: keys.projects(orgId),
    queryFn: () => api.get<Project[]>(`/organizations/${orgId}/projects`),
    enabled: Boolean(orgId),
  });
}

export function useProject(orgId: string, projectId: string) {
  return useQuery({
    queryKey: keys.project(orgId, projectId),
    queryFn: () => api.get<Project>(`/organizations/${orgId}/projects/${projectId}`),
    enabled: Boolean(orgId && projectId),
  });
}

export function useCreateProject(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; slug?: string }) =>
      api.post<Project>(`/organizations/${orgId}/projects`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.projects(orgId) });
      queryClient.invalidateQueries({ queryKey: keys.organizations });
    },
  });
}

export function useAddTarget(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: {
      language: number;
      packageName: string;
      registry?: number;
      autoPublish: boolean;
    }) => api.post<SdkTarget>(`/organizations/${orgId}/projects/${projectId}/targets`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) }),
  });
}

// --- specs ----------------------------------------------------------------------------

export function useSpecs(orgId: string, projectId: string) {
  return useQuery({
    queryKey: keys.specs(orgId, projectId),
    queryFn: () => api.get<SpecVersion[]>(`/organizations/${orgId}/projects/${projectId}/specs`),
    enabled: Boolean(orgId && projectId),
  });
}

export function useUploadSpec(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => {
      const formData = new FormData();
      formData.append("file", file);
      return api.upload<SpecUpload>(`/organizations/${orgId}/projects/${projectId}/specs`, formData);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.specs(orgId, projectId) });
      queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) });
      // An upload can auto-trigger runs for auto-publish targets.
      queryClient.invalidateQueries({ queryKey: ["organizations", orgId, "runs"] });
    },
  });
}

// --- runs -----------------------------------------------------------------------------

/** Poll while anything is in flight; stop once every run has settled. */
const POLL_INTERVAL_MS = 2000;

export function useRuns(orgId: string, projectId?: string) {
  const query: UseQueryOptions<Run[]> = {
    queryKey: keys.runs(orgId, projectId),
    queryFn: () =>
      api.get<Run[]>(
        `/organizations/${orgId}/runs${projectId ? `?projectId=${projectId}` : ""}`,
      ),
    enabled: Boolean(orgId),
    refetchInterval: (query) => {
      const runs = query.state.data;
      if (!runs) return false;
      return runs.some((r) => !isTerminal(r.status)) ? POLL_INTERVAL_MS : false;
    },
  };

  return useQuery(query);
}

export function useRun(orgId: string, runId: string) {
  return useQuery({
    queryKey: keys.run(orgId, runId),
    queryFn: () => api.get<Run>(`/organizations/${orgId}/runs/${runId}`),
    enabled: Boolean(orgId && runId),
    // A finished run is immutable, so polling it forever would be pure waste.
    refetchInterval: (query) =>
      query.state.data && !isTerminal(query.state.data.status) ? POLL_INTERVAL_MS : false,
  });
}

export function useCreateRun(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { specVersionId: string; sdkTargetId: string; publish: boolean }) =>
      api.post<Run>(`/organizations/${orgId}/runs`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organizations", orgId, "runs"] }),
  });
}

export function useCancelRun(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (runId: string) => api.post<Run>(`/organizations/${orgId}/runs/${runId}/cancel`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organizations", orgId, "runs"] }),
  });
}

export function useStepLog(orgId: string, runId: string, step: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.stepLog(orgId, runId, step),
    queryFn: () => api.get<string>(`/organizations/${orgId}/runs/${runId}/steps/${step}/log`),
    enabled,
    retry: false,
  });
}

// --- credentials ----------------------------------------------------------------------

export function useCredentials(orgId: string) {
  return useQuery({
    queryKey: keys.credentials(orgId),
    queryFn: () => api.get<Credential[]>(`/organizations/${orgId}/credentials`),
    enabled: Boolean(orgId),
  });
}

export function useCreateCredential(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { registry: number; label: string; token: string }) =>
      api.post<Credential>(`/organizations/${orgId}/credentials`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.credentials(orgId) }),
  });
}

export function useRevokeCredential(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentialId: string) =>
      api.delete<void>(`/organizations/${orgId}/credentials/${credentialId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.credentials(orgId) }),
  });
}
