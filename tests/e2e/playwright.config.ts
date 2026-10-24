import { defineConfig, devices } from '@playwright/test';
import { join } from 'node:path';
import { E2E_BASE_URL, e2eState } from './support/paths.ts';

const ci = Boolean(process.env.CI);

// Edge is installed on the development machine; CI installs Playwright's Chromium (D19, ADR-0011).
const browser = ci ? { ...devices['Desktop Chrome'] } : { ...devices['Desktop Edge'], channel: 'msedge' };

export default defineConfig({
  globalSetup: './global-setup.ts',
  outputDir: join(e2eState, 'results'),
  fullyParallel: true,
  forbidOnly: ci,
  // Flaky means broken (ROADMAP "How to use"): only the gate reruns, and only after a hung database.
  retries: 0,
  workers: ci ? 2 : 4,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: join(e2eState, 'report') }]],
  use: {
    baseURL: E2E_BASE_URL,
    locale: 'en-GB',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  // Each project joins together with its first spec (ADR-0011).
  projects: [
    {
      // Fast checks that the host, its people, its mail pickup and the form helpers work; every later suite builds on them.
      name: 'smoke',
      testDir: './specs/smoke',
      use: browser,
    },
    {
      // Sites that must not start: each test runs the built demo in a process of its own and reads its log; no browser.
      name: 'startup',
      testDir: './specs/startup',
    },
  ],
});
