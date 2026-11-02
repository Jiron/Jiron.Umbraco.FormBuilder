import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

// Where the browser tests find things, and the ports they use (ROADMAP section 4.11). Specs and scripts read ports and
// addresses from here and from the demo's appsettings.json, never from a copy ("One source for every value").

/** The repository root. */
export const repoRoot = resolve(import.meta.dirname, '..', '..', '..');

/** Git-ignored state of the browser test runs: hosts, mail, locks, reports and traces. */
export const e2eState = join(repoRoot, '.local', 'e2e');

/** The generated local credentials; never committed, never printed (CLAUDE.md rule 3). */
export const credentialsFile = join(repoRoot, '.local', 'credentials.json');

/** The demo site project, which the browser tests run as their host. */
export const demoProject = join(repoRoot, 'demo', 'Jiron.Umbraco.FormBuilder.Demo');

/** The texts the demo's seeder writes: the specs expect what it seeded. */
export const demoContentFile = join(demoProject, 'Seeding', 'DemoContent.json');

/** The client workspace (the website assets and the backoffice bundle the demo embeds). */
export const clientDir = join(repoRoot, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Client');

/** The Testing project, whose log lines the host's start-up waits for. */
export const testingProject = join(repoRoot, 'tests', 'Jiron.Umbraco.FormBuilder.Testing');

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

/** The browser tests' host, with a fresh database per run. */
export const E2E_PORT = 5520;
export const E2E_BASE_URL = `http://127.0.0.1:${E2E_PORT}`;

/** The static page of the headless tests (ROADMAP 6.6f): a site of its own that shows forms through the element. */
export const STATIC_PORT = 5521;
export const STATIC_ORIGIN = `http://127.0.0.1:${STATIC_PORT}`;

/**
 * The hosts that must refuse to start (ROADMAP 4.4a); they never listen, but each gets a port of its own, clear of the
 * static page's 5521 and the privacy host's 5522 (ROADMAP 4.11).
 */
export const REFUSAL_PORTS = { configuration: 5526, globalization: 5527 } as const;

/**
 * The host of the strict-site spec (ROADMAP 6.4k): its pages come from Umbraco's output cache, behind the Testing
 * project's Content Security Policy.
 */
export const CACHED_PORT = 5525;
export const CACHED_BASE_URL = `http://127.0.0.1:${CACHED_PORT}`;

/** The host of places-and-members (ROADMAP 7.1e), from Umbraco's output cache, beside the other specs' hosts. */
export const PLACES_PORT = 5528;
export const PLACES_BASE_URL = `http://127.0.0.1:${PLACES_PORT}`;

/** The host of protection-on-the-page (ROADMAP 7.2c), with fake keys and a stub of its own to verify through. */
export const PROTECTION_PORT = 5529;
export const PROTECTION_BASE_URL = `http://127.0.0.1:${PROTECTION_PORT}`;
export const PROTECTION_STUB_PORT = 5561;

/**
 * The host of the refused uploads (ROADMAP 7.3d, ADR-0006): the plain port its start-up wait reads, and the port with HTTPS
 * and HTTP/2, under a certificate of the Testing project's that nothing trusts, so its browser ignores that.
 */
export const SECURE_HOST_PORT = 5528;
export const SECURE_PORT = 5529;
export const SECURE_BASE_URL = `https://127.0.0.1:${SECURE_PORT}`;
export const SECURE_KEY_NAME = 'JironFormBuilderTestHost';

/** The HTTP stub that the test host starts for webhooks, URL option lists and captcha verification. */
export const STUB_PORT = 5560;
export const STUB_URL = `http://127.0.0.1:${STUB_PORT}`;
