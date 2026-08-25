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
