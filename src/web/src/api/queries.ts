import {
  keepPreviousData,
  skipToken,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { api } from "./client";
import {
  type Account,
  type Acknowledgement,
  type AuthResponse,
  type Credential,
  type DeletionBlocker,
  type Invitation,
  type InvitationPreview,
  type Member,
  type NotificationFeed,
  type NotificationPreference,
  type Organization,
  type Project,
  type Publication,
  type Run,
  type SdkTarget,
  type Session,
  type SpecContent,
  type SpecDiagnostic,
  type SpecList,
  type SpecUpload,
  isTerminal,
} from "./types";
import { useAuthStore } from "@/stores/auth";

/**
 * Query keys are built from this one place so an invalidation can never miss a cache entry
 * because of a typo in a hand-written key array — every key, so that a hand-written array
 * is a bug on sight.
 *
 * Shaped the way the query-key-factory pattern shapes them: a list and a detail never share a
 * prefix. `["organizations"]` used to be both the list and the root of every organization's
 * keys, so invalidating the list after a rename refetched every project, spec, run and
 * credential on screen — and the spec document held with an infinite staleTime along with
 * them. Now the list is `["organizations", "list"]`, and an organization's own keys sit under
 * its id; a mutation names the list, the detail, or the id's whole subtree, and gets that.
 */
export const keys = {
  organizations: ["organizations", "list"] as const,
  organization: (orgId: string) => ["organizations", orgId, "detail"] as const,
  projects: (orgId: string) => ["organizations", orgId, "projects", "list"] as const,
  project: (orgId: string, projectId: string) =>
    ["organizations", orgId, "projects", projectId, "detail"] as const,
  // The spec list is the root of each spec's findings and document on purpose: deleting or
  // revalidating one has to reach them, and there is no list-shaped read of either.
  specs: (orgId: string, projectId: string) =>
    ["organizations", orgId, "projects", projectId, "specs"] as const,
  specDiagnostics: (orgId: string, projectId: string, specId: string | null) =>
    [...keys.specs(orgId, projectId), specId, "diagnostics"] as const,
  specContent: (orgId: string, projectId: string, specId: string | null) =>
    [...keys.specs(orgId, projectId), specId, "content"] as const,
  publications: (orgId: string, projectId?: string) =>
    ["organizations", orgId, "publications", projectId ?? "all"] as const,
  members: (orgId: string) => ["organizations", orgId, "members"] as const,
  invitations: (orgId: string) => ["organizations", orgId, "invitations"] as const,
  // Addressed by token, not by organization: it is read before there is a membership.
  invitation: (token: string) => ["invitations", token] as const,
  // The root of every run query: a run created or deleted changes each list and, for the
  // one it is, the detail, so mutations invalidate this and lists and details sit under it.
  runs: (orgId: string) => ["organizations", orgId, "runs"] as const,
  runList: (orgId: string, projectId?: string) =>
    [...keys.runs(orgId), "list", projectId ?? "all"] as const,
  run: (orgId: string, runId: string) => [...keys.runs(orgId), "detail", runId] as const,
  stepLog: (orgId: string, runId: string, step: string) =>
    [...keys.run(orgId, runId), "log", step] as const,
  credentials: (orgId: string) => ["organizations", orgId, "credentials"] as const,
  account: ["account"] as const,
  sessions: ["sessions"] as const,
  deletionBlockers: ["deletion-blockers"] as const,
  notifications: ["notifications"] as const,
  // Not nested under `notifications`: React Query invalidates by prefix, and marking one
  // notification read would otherwise refetch the settings page too.
  notificationPreferences: ["notification-preferences"] as const,
};

// --- auth -----------------------------------------------------------------------------

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { email: string; password: string }) =>
      api.post<AuthResponse>("/auth/login", input),
    onSuccess: (data) => {
      setSession(data.accessToken, data.refreshToken);
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
      setSession(data.accessToken, data.refreshToken);
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

export function useForgotPassword() {
  return useMutation({
    mutationFn: (input: { email: string }) =>
      api.post<Acknowledgement>("/auth/forgot-password", input),
  });
}

export function useResetPassword() {
  return useMutation({
    mutationFn: (input: { token: string; newPassword: string }) =>
      api.post<Acknowledgement>("/auth/reset-password", input),
  });
}

// --- account --------------------------------------------------------------------------

export function useAccount() {
  return useQuery({
    queryKey: keys.account,
    queryFn: () => api.get<Account>("/account"),
  });
}

export function useUpdateProfile() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { displayName: string | null }) =>
      api.patch<Account>("/account", input),
    // The response is the account: written where everything that shows a name reads it.
    onSuccess: (account) => queryClient.setQueryData(keys.account, account),
  });
}

export function useChangePassword() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      api.post<void>("/account/password", input),
    // Changing the password revokes other sessions, so the list is now stale.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.sessions }),
  });
}

export function useSessions() {
  return useQuery({
    queryKey: keys.sessions,
    queryFn: () => api.get<Session[]>("/account/sessions"),
  });
}

export function useRevokeSession() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (sessionId: string) => api.delete<void>(`/account/sessions/${sessionId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.sessions }),
  });
}

export function useRevokeOtherSessions() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => api.delete<void>("/account/sessions"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.sessions }),
  });
}

export function useDeletionBlockers() {
  return useQuery({
    queryKey: keys.deletionBlockers,
    queryFn: () => api.get<DeletionBlocker[]>("/account/deletion-blockers"),
  });
}

export function useDeleteAccount() {
  const clear = useAuthStore((s) => s.clear);

  return useMutation({
    mutationFn: (input: { password: string }) =>
      api.delete<void>("/account", input),
    onSuccess: () => {
      clear();
      // A full reload rather than a route change: it drops any in-memory state a client-side
      // navigation would leave behind. Done here, on the hook, because the dialog that asked
      // is unmounted the moment it closes, and a callback handed to `mutate` goes with it.
      window.location.href = "/login";
    },
  });
}

// --- notifications --------------------------------------------------------------------

/**
 * The bell's contents.
 *
 * Polled rather than pushed. A run finishing is the one event nothing on this page can
 * observe — it happens in another process — and a minute is close enough for something whose
 * whole purpose is to be noticed later.
 */
export function useNotifications() {
  return useQuery({
    queryKey: keys.notifications,
    queryFn: () => api.get<NotificationFeed>("/notifications"),
    // Half a minute. A minute was long enough that a run finishing while somebody watched
    // the page looked like nothing had happened until they reloaded.
    refetchInterval: 30_000,
    // Both override the client-wide defaults, and this is the one query that earns it: its
    // whole job is to be current. Coming back to the tab is exactly when somebody wants to
    // know what they missed, and nothing here should ever be served as still-fresh.
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}

export function useDeleteNotification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/notifications/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.notifications }),
  });
}

/**
 * Empties the list. Unread notifications survive unless `includeUnread` says otherwise —
 * the API's default is the careful one, and the interface asks before overriding it.
 */
export function useClearNotifications() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (includeUnread: boolean) =>
      api.delete<void>(`/notifications?includeUnread=${includeUnread}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.notifications }),
  });
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/notifications/${id}/read`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.notifications }),
  });
}

export function useMarkAllNotificationsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<void>("/notifications/read-all"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.notifications }),
  });
}

/** Every switch the settings page shows, defaults already filled in by the API. */
export function useNotificationPreferences() {
  return useQuery({
    queryKey: keys.notificationPreferences,
    queryFn: () => api.get<NotificationPreference[]>("/notifications/preferences"),
  });
}

export function useSaveNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (preferences: NotificationPreference[]) =>
      api.put<NotificationPreference[]>("/notifications/preferences", { preferences }),
    onSuccess: (saved) => queryClient.setQueryData(keys.notificationPreferences, saved),
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

export function useRenameOrganization(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => api.patch<Organization>(`/organizations/${orgId}`, { name }),
    onSuccess: (organization) => {
      queryClient.setQueryData(keys.organization(orgId), organization);
      queryClient.invalidateQueries({ queryKey: keys.organizations });
    },
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

// --- members --------------------------------------------------------------------------

export function useMembers(orgId: string) {
  return useQuery({
    queryKey: keys.members(orgId),
    queryFn: () => api.get<Member[]>(`/organizations/${orgId}/members`),
    enabled: Boolean(orgId),
  });
}

export function useChangeMemberRole(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { userId: string; role: number }) =>
      api.patch<Member>(`/organizations/${orgId}/members/${input.userId}`, { role: input.role }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.members(orgId) }),
  });
}

export function useRemoveMember(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => api.delete<void>(`/organizations/${orgId}/members/${userId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.members(orgId) }),
  });
}

/**
 * The invitations still open.
 *
 * Listing them takes Admin — an outstanding invitation names an address, and who has been
 * asked to join is not general knowledge inside an organization — so the caller says whether
 * to ask at all rather than letting the page collect a 403 it would have to hide.
 */
export function useInvitations(orgId: string, enabled = true) {
  return useQuery({
    queryKey: keys.invitations(orgId),
    queryFn: () => api.get<Invitation[]>(`/organizations/${orgId}/members/invitations`),
    enabled: Boolean(orgId) && enabled,
  });
}

/**
 * Invites an address. Nobody joins until the link in the email is opened.
 *
 * Named for what it does now: this used to add whatever account already used the address,
 * which nothing had ever verified belonged to the person being invited.
 */
export function useInviteMember(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { email: string; role: number }) =>
      api.post<Invitation>(`/organizations/${orgId}/members`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.invitations(orgId) }),
  });
}

export function useRevokeInvitation(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (invitationId: string) =>
      api.delete<void>(`/organizations/${orgId}/members/invitations/${invitationId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.invitations(orgId) }),
  });
}

/**
 * A POST for a read: the token travels in the body rather than in a path, where it would be
 * written to every access log between here and the API.
 */
export function useInvitationPreview(token: string) {
  return useQuery({
    queryKey: keys.invitation(token),
    queryFn: () => api.post<InvitationPreview>("/invitations/preview", { token }),
    enabled: Boolean(token),
    // A token is spent once and the answer changes underneath nobody: asking twice buys
    // nothing, and a retry on a 404 would only repeat the refusal.
    staleTime: Infinity,
  });
}

/** Joins whichever organization the token was issued for; the caller does not name one. */
export function useAcceptInvitation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (token: string) => api.post<Member>("/invitations/accept", { token }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.organizations }),
  });
}

export function useDeclineInvitation() {
  return useMutation({
    mutationFn: (token: string) => api.post<void>("/invitations/decline", { token }),
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
    mutationFn: (input: { name: string; slug?: string; strictValidation?: boolean }) =>
      api.post<Project>(`/organizations/${orgId}/projects`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.projects(orgId) });
      queryClient.invalidateQueries({ queryKey: keys.organizations });
    },
  });
}

export function useRenameProject(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) =>
      api.patch<Project>(`/organizations/${orgId}/projects/${projectId}`, { name }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) });
      queryClient.invalidateQueries({ queryKey: keys.projects(orgId) });
    },
  });
}

/**
 * Turns the SDK-readiness checks on or off for a project.
 *
 * It changes what future validations conclude, not what past ones concluded: findings are
 * written once and read back from the row, so existing spec versions keep theirs until they
 * are revalidated.
 */
export function useSetStrictValidation(orgId: string, projectId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (enabled: boolean) =>
      api.put<Project>(
        `/organizations/${orgId}/projects/${projectId}/strict-validation`,
        { enabled },
      ),
    onSuccess: (project) => {
      // The response is the project: written before the refetch, so the switch reads the
      // new value the moment the mutation settles rather than the old one for a round trip.
      queryClient.setQueryData(keys.project(orgId, projectId), project);
      queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) });
      queryClient.invalidateQueries({ queryKey: keys.projects(orgId) });
    },
  });
}

export function useDeleteProject(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    // The name is typed back as confirmation and checked server-side too, so a caller that
    // skips the dialog gets the same answer.
    mutationFn: (input: { projectId: string; name: string }) =>
      api.delete<void>(`/organizations/${orgId}/projects/${input.projectId}`, { name: input.name }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.projects(orgId) });
      // The organization list carries a project count, as useCreateProject already knows.
      queryClient.invalidateQueries({ queryKey: keys.organizations });
    },
  });
}

// --- targets --------------------------------------------------------------------------

export function useAddTarget(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: {
      language: number;
      packageName: string;
      registry?: number;
      autoPublish: boolean;
      registryCredentialId?: string | null;
    }) => api.post<SdkTarget>(`/organizations/${orgId}/projects/${projectId}/targets`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) }),
  });
}

export function useUpdateTarget(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: {
      targetId: string;
      packageName: string;
      autoPublish: boolean;
      registryCredentialId?: string | null;
    }) =>
      api.patch<SdkTarget>(
        `/organizations/${orgId}/projects/${projectId}/targets/${input.targetId}`,
        {
          packageName: input.packageName,
          autoPublish: input.autoPublish,
          registryCredentialId: input.registryCredentialId ?? null,
        },
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) }),
  });
}

export function useDeleteTarget(orgId: string, projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { targetId: string; packageName: string }) =>
      api.delete<void>(
        `/organizations/${orgId}/projects/${projectId}/targets/${input.targetId}`,
        { name: input.packageName },
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.project(orgId, projectId) }),
  });
}

// --- specs ----------------------------------------------------------------------------

export function useSpecs(orgId: string, projectId: string) {
  return useQuery({
    queryKey: keys.specs(orgId, projectId),
    queryFn: () => api.get<SpecList>(`/organizations/${orgId}/projects/${projectId}/specs`),
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
      queryClient.invalidateQueries({ queryKey: keys.runs(orgId) });
    },
  });
}

/**
 * Runs today's validator over a document validated by an older one.
 *
 * Everything the dialog shows is invalidated together: the findings change, and so do the
 * counts and the declared versions on the specs table behind it.
 */
export function useRevalidateSpec(orgId: string, projectId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    // `strict` left out runs the project's own mode, which is what the plain button asks for.
    mutationFn: (input: { specId: string; strict?: boolean }) =>
      api.post<SpecUpload>(
        `/organizations/${orgId}/projects/${projectId}/specs/${input.specId}/revalidate`,
        { strict: input.strict ?? null },
      ),
    onSuccess: () => {
      // One call covers all three: the diagnostics and content keys nest under the specs
      // key, and React Query invalidates by prefix.
      queryClient.invalidateQueries({ queryKey: keys.specs(orgId, projectId) });
    },
  });
}

/**
 * Removes one uploaded document, with the runs executed against it.
 *
 * The server refuses once a package reached a registry: the package stays installable
 * whatever is deleted here, so the document it came from has to stay too.
 */
export function useDeleteSpec(orgId: string, projectId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (specId: string) =>
      api.delete<void>(`/organizations/${orgId}/projects/${projectId}/specs/${specId}`),
    onSuccess: () => {
      // Prefix invalidation covers the findings and the document alongside the table, and
      // every run list and detail: the runs executed against this version went with it.
      queryClient.invalidateQueries({ queryKey: keys.specs(orgId, projectId) });
      queryClient.invalidateQueries({ queryKey: keys.runs(orgId) });
    },
  });
}

export function useSpecDiagnostics(orgId: string, projectId: string, specId: string | null) {
  return useQuery({
    queryKey: keys.specDiagnostics(orgId, projectId, specId),
    // skipToken rather than `enabled`: the id is part of the URL, and a query function that
    // cannot be built without it should not exist without it either. Only fetched when a spec
    // is actually open: 413 findings is not a payload to prefetch.
    queryFn:
      orgId && projectId && specId
        ? () =>
            api.get<SpecDiagnostic[]>(
              `/organizations/${orgId}/projects/${projectId}/specs/${specId}/diagnostics`,
            )
        : skipToken,
  });
}

/**
 * The document a spec version was created from, so findings can be read against their source.
 *
 * Held for the session: it is immutable — a spec version's bytes never change — and it is the
 * one payload here big enough that refetching it on a remount would be felt.
 */
export function useSpecContent(orgId: string, projectId: string, specId: string | null) {
  return useQuery({
    queryKey: keys.specContent(orgId, projectId, specId),
    queryFn:
      orgId && projectId && specId
        ? () =>
            api.get<SpecContent>(
              `/organizations/${orgId}/projects/${projectId}/specs/${specId}/content`,
            )
        : skipToken,
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

// --- runs -----------------------------------------------------------------------------

/** Poll while anything is in flight; stop once every run has settled. */
const POLL_INTERVAL_MS = 2000;

export function useRuns(orgId: string, projectId?: string) {
  return useQuery({
    queryKey: keys.runList(orgId, projectId),
    queryFn: () =>
      api.get<Run[]>(
        `/organizations/${orgId}/runs${projectId ? `?projectId=${projectId}` : ""}`,
      ),
    // `undefined` asks for every run; `""` is a project filter whose id has not resolved yet,
    // and asking then answered "no filter" for a moment before answering the filter.
    enabled: Boolean(orgId) && projectId !== "",
    // The last list stays up while the filter changes, instead of a skeleton between two
    // tables that mostly share their rows.
    placeholderData: keepPreviousData,
    refetchInterval: (query) => {
      const runs = query.state.data;
      if (!runs) return false;
      return runs.some((r) => !isTerminal(r.status)) ? POLL_INTERVAL_MS : false;
    },
  });
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
    onSuccess: (run) => {
      // The response is the run the page navigates to: written to its detail before the
      // lists refetch, so the page opens on it rather than on a skeleton asking for it.
      queryClient.setQueryData(keys.run(orgId, run.id), run);
      queryClient.invalidateQueries({ queryKey: keys.runs(orgId) });
    },
  });
}

export function useCancelRun(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (runId: string) => api.post<Run>(`/organizations/${orgId}/runs/${runId}/cancel`),
    onSuccess: (run) => {
      queryClient.setQueryData(keys.run(orgId, run.id), run);
      queryClient.invalidateQueries({ queryKey: keys.runs(orgId) });
    },
  });
}

/**
 * The artifact of a run, fetched through the API and handed to the browser as a download.
 *
 * Through the API rather than from a presigned bucket URL. A presigned URL is a bearer token in
 * a link — whoever holds it reads the object until it expires, and the bucket has to be
 * reachable from a browser for it to work at all. Here the bytes only ever answer a request
 * carrying this session's token.
 *
 * A mutation, not a query: it is an action with a pending state and a failure to report, and
 * nothing should cache a tarball.
 */
export function useDownloadArtifact(orgId: string) {
  return useMutation({
    mutationFn: async ({ runId, fallbackName }: { runId: string; fallbackName: string }) => {
      const file = await api.download(`/organizations/${orgId}/runs/${runId}/artifact`);

      // A blob URL and a synthetic click: no popup, so nothing for a blocker to swallow, and
      // the page the user is reading stays where it is.
      const href = URL.createObjectURL(file.blob);
      const link = document.createElement("a");
      link.href = href;
      link.download = file.fileName ?? fallbackName;

      // Attached before clicking: Firefox ignores a click on an anchor that is not in the
      // document.
      document.body.appendChild(link);
      link.click();
      link.remove();

      // Revoked on a later task, not in this one: the save is still reading the blob when
      // click() returns, and revoking underneath it truncates the file.
      setTimeout(() => URL.revokeObjectURL(href), 0);
    },
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

// --- publications ---------------------------------------------------------------------

export function usePublications(orgId: string, projectId?: string) {
  return useQuery({
    queryKey: keys.publications(orgId, projectId),
    queryFn: () =>
      api.get<Publication[]>(
        `/organizations/${orgId}/publications${projectId ? `?projectId=${projectId}` : ""}`,
      ),
    // `undefined` asks for the organization's; `""` is a project whose id has not resolved
    // yet, and asking then fetched everything under a key nothing would read again.
    enabled: Boolean(orgId) && projectId !== "",
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
    mutationFn: (input: { registry: number; label: string; token: string; registryUrl?: string }) =>
      api.post<Credential>(`/organizations/${orgId}/credentials`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.credentials(orgId) }),
  });
}

export function useRestoreCredential(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentialId: string) =>
      api.post<Credential>(`/organizations/${orgId}/credentials/${credentialId}/restore`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.credentials(orgId) }),
  });
}

export function useDeleteCredential(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentialId: string) =>
      api.delete<void>(`/organizations/${orgId}/credentials/${credentialId}/permanently`),
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
