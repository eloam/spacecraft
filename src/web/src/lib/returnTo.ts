/** The query parameter carrying where the visitor was headed before they had a session. */
const PARAM = "next";

/**
 * Where to send someone once they are signed in.
 *
 * Only an in-application path is ever honoured. This value arrives from the URL bar, so an
 * absolute URL — or the protocol-relative `//elsewhere.test`, which a browser reads as one —
 * would turn our own sign-in page into a redirector to somebody else's. That is what a
 * phishing link is made of, and the address bar would say SpaceCraft the whole way. Anything
 * that does not resolve to this origin falls back to the dashboard.
 *
 * Judged by parsing it the way the browser will, not by the shapes we know about. The prefix
 * checks this replaces refused `//host` and `/\host`, and let `/\t/host` through: the URL
 * parser strips tabs and newlines before it reads the authority, so that one is `//host` to
 * it, and it was the one a link could carry as `%09`.
 */
export function returnPathIn(params: URLSearchParams, origin = window.location.origin): string {
  const next = params.get(PARAM);
  if (!next || !next.startsWith("/")) return "/";

  let url: URL;
  try {
    url = new URL(next, origin);
  } catch {
    return "/";
  }

  if (url.origin !== origin) return "/";
  return url.pathname + url.search + url.hash;
}

/** Points `path` at the sign-in pages so that signing in comes back to where we are now. */
export function withReturnTo(path: string, location: { pathname: string; search: string }): string {
  const here = location.pathname + location.search;
  return here === "/" ? path : `${path}?${PARAM}=${encodeURIComponent(here)}`;
}

/**
 * The query string that carries an in-flight destination across to the other authentication
 * page. Without it, someone who follows "No account? Create one" arrives at a register page
 * that has forgotten the invitation that sent them there.
 */
export function returnQueryIn(params: URLSearchParams): string {
  const next = params.get(PARAM);
  return next ? `?${PARAM}=${encodeURIComponent(next)}` : "";
}
