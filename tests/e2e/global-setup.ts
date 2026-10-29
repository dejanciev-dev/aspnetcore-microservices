import type { FullConfig } from '@playwright/test';

const TIMEOUT_MS = 180_000;

/**
 * Containers report "started" before the services behind nginx can answer. Wait until the catalog
 * and the BFF respond through the storefront's origin, so the first test doesn't hit a cold stack.
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0].use.baseURL!;
  const probes = ['/api/products?pageSize=1', '/bff/user'].map((path) => new URL(path, baseURL).toString());
  const deadline = Date.now() + TIMEOUT_MS;
  let lastProblem = '';

  for (const url of probes) {
    for (;;) {
      try {
        const response = await fetch(url);
        if (response.ok) break;
        lastProblem = `${url} answered ${response.status}`;
      } catch (error) {
        lastProblem = `${url} is unreachable (${(error as Error).message})`;
      }

      if (Date.now() > deadline) throw new Error(`The storefront stack is not ready: ${lastProblem}`);
      await new Promise((resolve) => setTimeout(resolve, 2_000));
    }
  }
}
