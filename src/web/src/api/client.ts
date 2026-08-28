import { useAuthStore } from "@/stores/auth";
import type { AuthResponse } from "./types";

const BASE_URL = "/api/v1";

/** An error carrying the RFC 9457 problem details the API returned. */
export class ApiError extends Error {
  // Declared as fields rather than constructor parameter properties: the project builds
  // with `erasableSyntaxOnly`, which only permits TypeScript syntax that erases to nothing.
  readonly status: number;
  readonly title: string;
  readonly detail: string;
  readonly errors?: Record<string, string[]>;

  constructor(status: number, title: string, detail: string, errors?: Record<string, string[]>) {
    super(detail || title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.errors = errors;
  }

  /** First validation message for a field, if the API reported one. */
  fieldError(field: string): string | undefined {
    const key = Object.keys(this.errors ?? {}).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? this.errors?.[key]?.[0] : undefined;
  }
}

/**
 * A single in-flight refresh, shared by every request that hits a 401 at once.
 * Without this, a dashboard firing five parallel queries on a stale token would start five
 * refreshes — and since refresh tokens rotate and are single-use, four of them would be
 * rejected as replay and log the user out.
 */
let refreshInFlight: Promise<boolean> | null = null;

async function refreshSession(): Promise<boolean> {
  const { refreshToken } = useAuthStore.getState();
  if (!refreshToken) return false;

  const response = await fetch(`${BASE_URL}/auth/refresh`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });

  if (!response.ok) {
    useAuthStore.getState().clear();
    return false;
  }

  const payload = (await response.json()) as AuthResponse;
  useAuthStore.getState().updateTokens(payload.accessToken, payload.refreshToken);
  return true;
}

function ensureRefresh(): Promise<boolean> {
  refreshInFlight ??= refreshSession().finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

interface RequestOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
  /** Set for multipart uploads, where the browser must pick the boundary itself. */
  formData?: FormData;
}

async function send(path: string, options: RequestOptions, retryOn401 = true): Promise<Response> {
  const { accessToken } = useAuthStore.getState();
  const headers = new Headers(options.headers);

  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);

  let body: BodyInit | undefined;
  if (options.formData) {
    body = options.formData;
    // Deliberately no Content-Type: fetch adds the multipart boundary only if we don't.
  } else if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(options.body);
  }

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers, body });

  if (response.status === 401 && retryOn401 && useAuthStore.getState().refreshToken) {
    const refreshed = await ensureRefresh();
    if (refreshed) return send(path, options, false);
  }

  return response;
}

async function toError(response: Response): Promise<ApiError> {
  let title = response.statusText || `Request failed (${response.status})`;
  let detail = "";
  let errors: Record<string, string[]> | undefined;

  try {
    const problem = await response.json();
    title = problem.title ?? title;
    detail = problem.detail ?? "";
    errors = problem.errors;

    // Validation problems put the useful text in `errors`, not `detail`.
    if (!detail && errors) {
      detail = Object.values(errors).flat().join(" ");
    }
  } catch {
    // Non-JSON error body (a proxy error page, say). The status alone will have to do.
  }

  // Every call site reports `detail`, and a problem document is free to leave it out — a 405
  // from a route that lost a verb, a 502 from a proxy in front of the API. Without this the
  // user got a toast with nothing written in it, which says less than the status would.
  return new ApiError(response.status, title, detail || title, errors);
}

async function parse<T>(response: Response): Promise<T> {
  if (!response.ok) throw await toError(response);
  if (response.status === 204) return undefined as T;

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) return (await response.json()) as T;

  return (await response.text()) as T;
}

export const api = {
  get: <T>(path: string) => send(path, { method: "GET" }).then((r) => parse<T>(r)),
  post: <T>(path: string, body?: unknown) => send(path, { method: "POST", body }).then((r) => parse<T>(r)),
  put: <T>(path: string, body?: unknown) => send(path, { method: "PUT", body }).then((r) => parse<T>(r)),
  patch: <T>(path: string, body?: unknown) => send(path, { method: "PATCH", body }).then((r) => parse<T>(r)),
  // DELETE carries a body here: deleting an account is confirmed with a password, and a
  // password does not belong in a query string where it would land in access logs.
  delete: <T>(path: string, body?: unknown) => send(path, { method: "DELETE", body }).then((r) => parse<T>(r)),
  upload: <T>(path: string, formData: FormData) =>
    send(path, { method: "POST", formData }).then((r) => parse<T>(r)),
};
