import { API_URL } from "./config";

/**
 * Waits for the stack to be able to answer before the first test asks it anything.
 *
 * The API migrates the database on startup, so "the container is running" and "the
 * application is ready" are half a dozen seconds apart. Probed from here rather than by a
 * container healthcheck: the runtime image carries no HTTP client to probe itself with.
 */
export default async function waitForTheApi(): Promise<void> {
  const deadline = Date.now() + 120_000;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${API_URL}/health`);
      if (response.ok) return;
    } catch {
      // Not up yet. The loop is the whole point.
    }

    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }

  throw new Error(
    `${API_URL} never became ready. Is the stack up? ` +
      "docker compose -f compose.e2e.yaml up -d --wait",
  );
}
