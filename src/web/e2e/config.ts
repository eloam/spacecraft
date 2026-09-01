/**
 * Where the stack under test lives.
 *
 * Defaults match compose.e2e.yaml, which deliberately does not use the ports the development
 * stack uses: a campaign should never be able to write into the database somebody is working
 * in, and picking the wrong port is the easiest way for that to happen.
 */
// 127.0.0.1 rather than localhost: Docker publishes on IPv4 only, and Node resolves
// localhost to ::1 first — which fails before it ever tries the address that answers.
export const API_URL = process.env.E2E_API_URL ?? "http://127.0.0.1:58080";
export const WEB_URL = process.env.E2E_WEB_URL ?? "http://localhost:4173";
export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? "http://127.0.0.1:58025";
