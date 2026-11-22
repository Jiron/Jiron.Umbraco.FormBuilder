import { defineConfig, devices, type PlaywrightTestConfig } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { E2E_BASE_URL, E2E_SITE, e2eState } from './support/paths.ts';
import { isShared, planSites, readSpecTimes, siteMatch, SPEC_TIMES_FILE, timesBelow } from './support/sites.ts';

const ci = Boolean(process.env.CI);

// Edge is installed on the development machine; CI installs Playwright's Chromium (D19, ADR-0011).
const browser = ci ? { ...devices['Desktop Chrome'] } : { ...devices['Desktop Edge'], channel: 'msedge' };

type Project = NonNullable<PlaywrightTestConfig['projects']>[number];

/**
 * The projects as one site runs them (support/sites.ts, ROADMAP 24.1). The one site of a default run runs every spec
 * file, as always. One of several sites runs its share of the spec files, weighed by spec-times.json, and the last one
 * every spec file the others do not name; `budgets` runs on none of them but on the budgets site, alone and after
 * them, where it is the only project with tests (ADR-0169). The order of the projects inside a site stays as below.
 */
function onThisSite(projects: Project[]): Project[] {
  if (E2E_SITE.budgets)
    return projects.map((project) => (project.name === 'budgets' ? project : { ...project, testMatch: [] }));
  if (!isShared(E2E_SITE)) return projects;
  const shared = projects.filter((project) => project.name !== 'budgets');
  const timesFile = join(import.meta.dirname, SPEC_TIMES_FILE);
  const times = readSpecTimes(existsSync(timesFile) ? readFileSync(timesFile, 'utf8') : null);
  const folders = shared.map((project) => (project.testDir ?? '').replace(/^\.\//, ''));
  const match = siteMatch(planSites(timesBelow(times, folders), E2E_SITE.count), E2E_SITE);
  return projects.map((project) =>
    project.name === 'budgets' ? { ...project, testMatch: [] } : { ...project, ...match },
  );
}

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
  // The JSON results are what scripts/e2e-gate.mjs adds up: every test once, across the sites.
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: join(e2eState, 'report') }],
    ['json', { outputFile: join(e2eState, 'results.json') }],
  ],
  use: {
    baseURL: E2E_BASE_URL,
    locale: 'en-GB',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  // Each project joins together with its first spec (ADR-0011).
  projects: onThisSite([
    {
      // Fast checks that the host, its people, its mail pickup and the form helpers work; every later suite builds on them.
      name: 'smoke',
      testDir: './specs/smoke',
      use: browser,
    },
    {
      // A visitor's pages on the demo site: its forms without JavaScript, posted back to their page (ADR-0100).
      name: 'website',
      testDir: './specs/website',
      use: browser,
    },
    {
      // The Forms section in the backoffice, signed in as each persona (ADR-0165).
      name: 'backoffice',
      testDir: './specs/backoffice',
      use: browser,
    },
    {
      // What uses up a limit of the one host, after every spec that could meet it (ADR-0137).
      name: 'limits',
      testDir: './specs/limits',
      dependencies: ['website'],
      use: browser,
    },
    {
      // What every page and screen of the one host reads: settings and access saved from their screens, each put back
      // after its test. After every spec that reads them, so none meets a value while it is changed (ADR-0221).
      name: 'site-wide',
      testDir: './specs/site-wide',
      dependencies: ['smoke', 'website', 'backoffice', 'limits'],
      use: browser,
    },
    {
      // Interactions measured against section 4.12's budgets: alone, after every other project, so nothing else shares
      // the machine while they are measured (ADR-0169). Without a trace: its recorder walks the page's whole DOM on the
      // page's own thread at every action, which the measure would count as the designer's (160 to 210 ms on a form of
      // 300 fields, 10.1b); a failure says every slow interaction in its message instead.
      name: 'budgets',
      testDir: './specs/budgets',
      dependencies: ['smoke', 'website', 'backoffice', 'limits', 'site-wide', 'startup'],
      workers: 1,
      use: { ...browser, trace: 'off' },
    },
    // A phase review's UX walk, only when asked (JFB_E2E_UX=1): every screen in twelve ways, then a sheet per screen
    // (support/ux-walk.ts; ROADMAP 9.6a). The gate leaves it out: it reviews, it doesn't check.
    ...(process.env['JFB_E2E_UX']
      ? [
          { name: 'ux', testDir: './specs/ux', use: browser },
          { name: 'ux-sheets', testDir: './specs/ux-sheets', dependencies: ['ux'], use: browser },
          // The visitor's side in the seven languages, after the walk, as it adds the five languages the demo lacks.
          { name: 'ux-languages', testDir: './specs/ux-languages', dependencies: ['ux'], use: browser },
        ]
      : []),
    // Two hosts on one SQL Server database in CI's load-balancing job; JFB_E2E_LB=1 proves the suite itself on one host.
    ...(process.env['SA_PASSWORD'] || process.env['JFB_E2E_LB']
      ? [{ name: 'lb', testDir: './specs/lb', use: browser }]
      : []),
    {
      // Sites that must not start: each test runs the built demo in a process of its own and reads its log; no browser.
      name: 'startup',
      testDir: './specs/startup',
    },
  ]),
});
