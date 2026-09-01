import { useSearchParams } from "react-router-dom";

/** The query parameter carrying where the visitor was headed before they had a session. */
const PARAM = "next";

/**
 * Where to send someone once they are signed in.
 *
 * Only an in-application path is ever honoured. This value arrives from the URL bar, so an
 * absolute URL — or the protocol-relative `//elsewhere.test`, which a browser reads as one —
 * would turn our own sign-in page into a redirector to somebody else's. That is what a
 * phishing link is made of, and the address bar would say SpaceCraft the whole way. Anything
 * that is not a plain path falls back to the dashboard.
 */
export function returnPathIn(params: URLSearchParams): string {
  const next = params.get(PARAM);
  if (!next || !next.startsWith("/")) return "/";
  // Both "//host" and "/\host" are parsed as an authority rather than a path.
  if (next.startsWith("//") || next.startsWith("/\\")) return "/";
  return next;
}

/** Where the current page should continue to once there is a session. */
export function useReturnPath(): string {
  const [params] = useSearchParams();
  return returnPathIn(params);
}

/** Points `path` at the sign-in pages so that signing in comes back to where we are now. */
export function withReturnTo(path: string, location: { pathname: string; search: string }): string {
  const here = location.pathname + location.search;
  return here === "/" ? path : `${path}?${PARAM}=${encodeURIComponent(here)}`;
}

/**
 * Carries an in-flight destination across to the other authentication page. Without this,
 * someone who follows "No account? Create one" arrives at a register page that has forgotten
 * the invitation that sent them there.
 */
export function useReturnQuery(): string {
  const [params] = useSearchParams();
  const next = params.get(PARAM);
  return next ? `?${PARAM}=${encodeURIComponent(next)}` : "";
}
