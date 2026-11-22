import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import {
  CACHED_PORTS,
  FRESH_PORTS,
  HOST_PORTS,
  LB_FIRST_PORTS,
  LB_SECOND_PORTS,
  LOCKS_PORTS,
  PAUSED_PORTS,
  PLACES_PORTS,
  PROTECTION_PORTS,
  PROTECTION_STUB_PORTS,
  REFUSAL_CONFIGURATION_PORTS,
  REFUSAL_GLOBALIZATION_PORTS,
  SECURE_HOST_PORTS,
  SECURE_PORTS,
  siteCredentialsKey,
  siteFromEnv,
  sitePort,
  siteState,
  SITES_PORTS,
  STATIC_PORTS,
  STUB_PORTS,
  type Site,
} from './sites.ts';

// Where the browser tests find things, and the ports they use (ROADMAP section 4.11). Specs and scripts read ports and
// addresses from here and from the demo's appsettings.json, never from a copy ("One source for every value"). The
// ports and the state folder are this process's site's (support/sites.ts): site 1's, as always, unless
// scripts/e2e-gate.mjs runs several sites side by side.

/** The repository root. */
export const repoRoot = resolve(import.meta.dirname, '..', '..', '..');

/** The site this process belongs to: `JFB_E2E_SITES` and `JFB_E2E_SITE`, one site by default. */
export const E2E_SITE: Site = siteFromEnv(process.env);

/** Git-ignored state of this site's runs: hosts, mail, locks, reports and traces (.local/e2e/ for the one site). */
export const e2eState = siteState(repoRoot, E2E_SITE);

/** The generated local credentials; never committed, never printed (CLAUDE.md rule 3). */
export const credentialsFile = join(repoRoot, '.local', 'credentials.json');

/** This site's entry in the credentials file ("e2e" for the one site). */
export const CREDENTIALS_KEY = siteCredentialsKey(E2E_SITE);

/** The demo site project, which the browser tests run as their host. */
export const demoProject = join(repoRoot, 'demo', 'Jiron.Umbraco.FormBuilder.Demo');

/** The texts the demo's seeder writes: the specs expect what it seeded. */
export const demoContentFile = join(demoProject, 'Seeding', 'DemoContent.json');

/** The client workspace (the website assets and the backoffice bundle the demo embeds). */
export const clientDir = join(repoRoot, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Client');

/** The Testing project, whose log lines the host's start-up waits for. */
export const testingProject = join(repoRoot, 'tests', 'Jiron.Umbraco.FormBuilder.Testing');

/** The AI add-on's test-only project, whose fake provider the host offers (ADR-0018). */
export const testingAIProject = join(repoRoot, 'tests', 'Jiron.Umbraco.FormBuilder.Testing.AI');

/** The target framework of every project, from Directory.Build.props. */
export const TARGET_FRAMEWORK: string = (() => {
  const props = readFileSync(join(repoRoot, 'Directory.Build.props'), 'utf8');
  const found = /<TargetFramework>([^<]+)<\/TargetFramework>/.exec(props)?.[1];
  if (!found) throw new Error('Directory.Build.props names no TargetFramework.');
  return found;
})();

/** The address of the demo that scripts/demo.ps1 starts, from the demo's own settings; no test host uses its port. */
export const DEMO_URL: string = JSON.parse(readFileSync(join(demoProject, 'appsettings.json'), 'utf8')).Umbraco.CMS
  .WebRouting.UmbracoApplicationUrl;

/** The ports Jiron Form Builder's servers may use (CLAUDE.md rule 6). */
export const PORT_RANGE = { first: 5500, last: 5599 } as const;

/** The browser tests' host, with a fresh database per run: 5520 on site 1. */
export const E2E_PORT = sitePort(HOST_PORTS, E2E_SITE);
export const E2E_BASE_URL = `http://127.0.0.1:${E2E_PORT}`;

/** The static page of the headless tests (ROADMAP 6.6f): a site of its own that shows forms through the element. */
export const STATIC_PORT = sitePort(STATIC_PORTS, E2E_SITE);
export const STATIC_ORIGIN = `http://127.0.0.1:${STATIC_PORT}`;

/**
 * The hosts that must refuse to start (ROADMAP 4.4a); they never listen, but each gets a port of its own, clear of the
 * static page's and the privacy host's (ROADMAP 4.11).
 */
export const REFUSAL_PORTS = {
  configuration: sitePort(REFUSAL_CONFIGURATION_PORTS, E2E_SITE),
  globalization: sitePort(REFUSAL_GLOBALIZATION_PORTS, E2E_SITE),
} as const;

/**
 * The host of the strict-site spec (ROADMAP 6.4k): its pages come from Umbraco's output cache, behind the Testing
 * project's Content Security Policy.
 */
export const CACHED_PORT = sitePort(CACHED_PORTS, E2E_SITE);
export const CACHED_BASE_URL = `http://127.0.0.1:${CACHED_PORT}`;

/**
 * The load test's host (ROADMAP 16.2, ADR-0011, ADR-0238), with a database of its own and the load's forms and entries.
 * It is no site's: the load test runs on its own, never inside a browser run.
 */
export const LOAD_PORT = 5523;
export const LOAD_BASE_URL = `http://127.0.0.1:${LOAD_PORT}`;

/** The host of places-and-members (ROADMAP 7.1e), from Umbraco's output cache, beside the other specs' hosts. */
export const PLACES_PORT = sitePort(PLACES_PORTS, E2E_SITE);
export const PLACES_BASE_URL = `http://127.0.0.1:${PLACES_PORT}`;

/** The host of protection-on-the-page (ROADMAP 7.2c), with fake keys and a stub of its own to verify through. */
export const PROTECTION_PORT = sitePort(PROTECTION_PORTS, E2E_SITE);
export const PROTECTION_BASE_URL = `http://127.0.0.1:${PROTECTION_PORT}`;
export const PROTECTION_STUB_PORT = sitePort(PROTECTION_STUB_PORTS, E2E_SITE);

/** The two hosts of the load-balancing suite, which CI starts on one SQL Server database (ROADMAP 8.1). */
export const LB_PORTS = [sitePort(LB_FIRST_PORTS, E2E_SITE), sitePort(LB_SECOND_PORTS, E2E_SITE)] as const;

/** The host of designer-locks (ROADMAP 9.4a, ADR-0060), whose configuration locks settings of the text type. */
export const LOCKS_PORT = sitePort(LOCKS_PORTS, E2E_SITE);
export const LOCKS_BASE_URL = `http://127.0.0.1:${LOCKS_PORT}`;

/** The host of Deliveries (ROADMAP 12.4b, ADR-0219), which pauses sending, so its seeded deliveries stay as they are. */
export const PAUSED_PORT = sitePort(PAUSED_PORTS, E2E_SITE);
export const PAUSED_BASE_URL = `http://127.0.0.1:${PAUSED_PORT}`;

/** The host of the setup checklist's run (ROADMAP 12.6): the demo reset, without its content, its team or a From address. */
export const FRESH_PORT = sitePort(FRESH_PORTS, E2E_SITE);
export const FRESH_BASE_URL = `http://127.0.0.1:${FRESH_PORT}`;

/**
 * The host of two sites on their own names, site-a.localhost and site-b.localhost, each loopback by itself in Edge and
 * Chromium (ROADMAP 14.4, ADR-0232); its API calls send the Host header themselves, as the API request context resolves
 * those names to IPv6, where no host listens.
 */
export const SITES_PORT = sitePort(SITES_PORTS, E2E_SITE);
export const SITES_BASE_URL = `http://127.0.0.1:${SITES_PORT}`;

/**
 * The host of the refused uploads (ROADMAP 7.3d, ADR-0006): the plain port its start-up wait reads, and the port with HTTPS
 * and HTTP/2, under a certificate of the Testing project's that nothing trusts, so its browser ignores that.
 */
export const SECURE_HOST_PORT = sitePort(SECURE_HOST_PORTS, E2E_SITE);
export const SECURE_PORT = sitePort(SECURE_PORTS, E2E_SITE);
export const SECURE_BASE_URL = `https://127.0.0.1:${SECURE_PORT}`;
export const SECURE_KEY_NAME = 'JironFormBuilderTestHost';

/** The HTTP stub that the test host starts for webhooks, URL option lists and captcha verification. */
export const STUB_PORT = sitePort(STUB_PORTS, E2E_SITE);
export const STUB_URL = `http://127.0.0.1:${STUB_PORT}`;

/** The secret the test host's webhooks are signed with: a made-up one, as this build uses no real keys. */
export const E2E_WEBHOOK_SECRET = 'JFB_FAKE_e2e_webhook_signing';
