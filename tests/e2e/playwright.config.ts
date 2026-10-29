import { defineConfig, devices } from '@playwright/test';

// The storefront as a browser sees it. Docker Compose (nginx) and `ng serve` both serve it on 4200.
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:4200';

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',   // waits until the stack answers through the storefront
  outputDir: './test-results',

  // One worker: ada and grace each have one basket, and several tests change it.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },

  reporter: process.env.CI
    ? [['github'], ['list'], ['html', { open: 'never' }]]
    : [['list'], ['html', { open: 'never' }]],

  use: {
    ...devices['Desktop Chrome'],
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [
    // Needs only the storefront, gateway and Catalog: runs against `dotnet run` + `ng serve` too.
    { name: 'anonymous', testDir: './tests/anonymous' },

    // Needs Keycloak (the full Compose or Aspire stack). Signs ada and grace in once and reuses the sessions.
    { name: 'setup', testMatch: 'auth.setup.ts' },
    { name: 'authenticated', testDir: './tests/authenticated', dependencies: ['setup'] },
  ],
});
