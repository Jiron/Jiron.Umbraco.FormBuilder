import { join } from 'node:path';

// How one run of the browser tests is shared out over several sites side by side (ROADMAP 24.1, ADR-0261). A site is
// a whole copy of what a run has: its own demo host with its own database, mail pickup folder and log, its own ports
// for every server a spec starts, its own sign-in lock, credentials, results and report. One site, the default, is the
// run as it always was: ports 5520 and up, state in .local/e2e/. scripts/e2e-gate.mjs starts the sites and checks that
// every test ran once; this module is the one place a site's ports, folders and spec files are decided.

/** The most sites a run may start: three blocks of ports fit in Jiron Form Builder's range (ROADMAP section 4.11). */
export const MAX_SITES = 3;

// Every server of a site, with its port on sites 1, 2 and 3. Site 1's are the ports the harness always had; sites 2 and
// 3 take 5562 to 5578 and 5580 to 5596, in the same order, which no other script uses. scripts/gate-clone.mjs and
// scripts/verify-clean.mjs read these lines to see that the ports are free before a gate.

/** The run's demo host. */
export const HOST_PORTS = [5520, 5562, 5580] as const;
/** The static page of the headless tests. */
export const STATIC_PORTS = [5521, 5563, 5581] as const;
/** The strict site behind the output cache. */
export const CACHED_PORTS = [5525, 5564, 5582] as const;
/** The host that refuses an invalid configuration (it never listens). */
export const REFUSAL_CONFIGURATION_PORTS = [5526, 5565, 5583] as const;
/** The host that refuses invariant globalization (it never listens). */
export const REFUSAL_GLOBALIZATION_PORTS = [5527, 5566, 5584] as const;
/** The refused uploads' host: the plain port its start-up wait reads. */
export const SECURE_HOST_PORTS = [5528, 5567, 5585] as const;
/** The refused uploads' host: HTTPS and HTTP/2. */
export const SECURE_PORTS = [5529, 5568, 5586] as const;
/** The places-and-members host. */
export const PLACES_PORTS = [5531, 5569, 5587] as const;
/** The protection-on-the-page host. */
export const PROTECTION_PORTS = [5532, 5570, 5588] as const;
/** The first host of the load-balancing suite. */
export const LB_FIRST_PORTS = [5533, 5571, 5589] as const;
/** The second host of the load-balancing suite. */
export const LB_SECOND_PORTS = [5534, 5572, 5590] as const;
/** The designer-locks host. */
export const LOCKS_PORTS = [5535, 5573, 5591] as const;
/** The Deliveries host, with sending paused. */
export const PAUSED_PORTS = [5536, 5574, 5592] as const;
/** The setup checklist's fresh demo. */
export const FRESH_PORTS = [5537, 5575, 5593] as const;
/** The host of two sites on their own names. */
export const SITES_PORTS = [5538, 5576, 5594] as const;
/** The stub the run's host starts for webhooks, URL option lists and captcha verification. */
export const STUB_PORTS = [5560, 5577, 5595] as const;
/** The protection host's own stub. */
export const PROTECTION_STUB_PORTS = [5561, 5578, 5596] as const;

/** Every server of a site and its ports on the sites, by name. */
export const SITE_PORT_TABLE = {
  host: HOST_PORTS,
  static: STATIC_PORTS,
  cached: CACHED_PORTS,
  refusalConfiguration: REFUSAL_CONFIGURATION_PORTS,
  refusalGlobalization: REFUSAL_GLOBALIZATION_PORTS,
  secureHost: SECURE_HOST_PORTS,
  secure: SECURE_PORTS,
  places: PLACES_PORTS,
  protection: PROTECTION_PORTS,
  lbFirst: LB_FIRST_PORTS,
  lbSecond: LB_SECOND_PORTS,
  locks: LOCKS_PORTS,
  paused: PAUSED_PORTS,
  fresh: FRESH_PORTS,
  sites: SITES_PORTS,
  stub: STUB_PORTS,
  protectionStub: PROTECTION_STUB_PORTS,
} as const;

/**
 * Which site this process belongs to. `index` counts from 1 to `count`. The budgets site runs the `budgets` project
 * alone after every other site has ended (ADR-0169): on site 1's ports, which are free by then, with a state of its own.
 */
export interface Site {
  index: number;
  count: number;
  budgets: boolean;
}

/** The site of a run of one site: the default, exactly the run as it always was. */
export const ONE_SITE: Site = { index: 1, count: 1, budgets: false };

/**
 * The site from the environment: `JFB_E2E_SITES` (how many, 1 by default) and `JFB_E2E_SITE` (which, 1 by default, or
 * `budgets`). scripts/e2e-gate.mjs sets both for each site's process.
 */
export function siteFromEnv(env: NodeJS.ProcessEnv): Site {
  const countText = (env['JFB_E2E_SITES'] ?? '1').trim();
  const indexText = (env['JFB_E2E_SITE'] ?? '1').trim().toLowerCase();
  const count = Number(countText);
  if (!/^\d+$/.test(countText) || count < 1 || count > MAX_SITES)
    throw new Error(`JFB_E2E_SITES is "${countText}"; use a whole number from 1 to ${MAX_SITES}.`);
  if (indexText === 'budgets') {
    if (count !== 1) throw new Error('JFB_E2E_SITE=budgets is a run of its own; leave JFB_E2E_SITES out.');
    return { index: 1, count: 1, budgets: true };
  }
  const index = Number(indexText);
  if (!/^\d+$/.test(indexText) || index < 1 || index > count)
    throw new Error(`JFB_E2E_SITE is "${indexText}"; use a whole number from 1 to ${count}, or "budgets".`);
  return { index, count, budgets: false };
}

/** Whether the site runs only a share of the spec files: one of several sites. */
export function isShared(site: Site): boolean {
  return !site.budgets && site.count > 1;
}

/** A site's port for one server. */
export function sitePort(ports: readonly number[], site: Site): number {
  const port = ports[site.budgets ? 0 : site.index - 1];
  if (port === undefined) throw new Error(`No port for site ${site.index}.`);
  return port;
}

/** Every port of site `index` (1 to MAX_SITES), in the table's order. */
export function portsOfSite(index: number): number[] {
  return Object.values(SITE_PORT_TABLE).map((ports) => {
    const port = ports[index - 1];
    if (port === undefined) throw new Error(`No ports for site ${index}.`);
    return port;
  });
}

/**
 * The folder of a site's state below the repository: .local/e2e/ for the one site of a default run (as always),
 * .local/e2e/site-<n>/ for one of several, .local/e2e/budgets/ for the budgets site.
 */
export function siteState(repoRoot: string, site: Site): string {
  const base = join(repoRoot, '.local', 'e2e');
  if (site.budgets) return join(base, 'budgets');
  return site.count === 1 ? base : join(base, `site-${site.index}`);
}

/** The entry of .local/credentials.json that holds a site's generated people: "e2e" for the default run. */
export function siteCredentialsKey(site: Site): string {
  if (site.budgets) return 'e2e-budgets';
  return site.count === 1 ? 'e2e' : `e2e-site-${site.index}`;
}

/** How logs name a site. */
export function siteLabel(site: Site): string {
  if (site.budgets) return 'budgets';
  return `site ${site.index}/${site.count}`;
}

// ---------------------------------------------------------------------------------------------------------------
// The spec files' shares
// ---------------------------------------------------------------------------------------------------------------

/** The seconds per spec file that weigh the sites, relative to tests/e2e. */
export const SPEC_TIMES_FILE = 'spec-times.json';

/** Seconds per spec file, keyed by its path from tests/e2e with forward slashes, such as `specs/smoke/site.spec.ts`. */
export type SpecTimes = Record<string, number>;

/** The seconds per spec file from the times file's text; a missing file is an empty map. */
export function readSpecTimes(text: string | null | undefined): SpecTimes {
  if (text === null || text === undefined) return {};
  const parsed = JSON.parse(text) as { specs?: unknown };
  const specs = parsed?.specs;
  if (!specs || typeof specs !== 'object') throw new Error(`${SPEC_TIMES_FILE} has no "specs" object`);
  for (const [spec, seconds] of Object.entries(specs)) {
    if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0)
      throw new Error(`${SPEC_TIMES_FILE}: ${spec} has no number of seconds`);
  }
  return specs as SpecTimes;
}

/** The times file's text: sorted by path, to a tenth of a second. */
export function formatSpecTimes(times: SpecTimes, sites: number): string {
  const specs = Object.fromEntries(
    Object.entries(times)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([spec, seconds]) => [spec, Math.max(0.1, Math.round(seconds * 10) / 10)]),
  );
  const document = {
    about:
      "Seconds per browser spec file (the sum of its tests' own times), which weigh the sites of scripts/e2e-gate.mjs " +
      `(ADR-0261). Written by its --record from a run on ${sites} site(s); a spec file missing here still runs, on the last site.`,
    specs,
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

/** The times of the spec files below the given folders (paths from tests/e2e), as the shares weigh them. */
export function timesBelow(times: SpecTimes, folders: readonly string[]): SpecTimes {
  const prefixes = folders.map((folder) => `${folder.replace(/\\/g, '/').replace(/\/+$/, '')}/`);
  return Object.fromEntries(
    Object.entries(times).filter(([spec]) => prefixes.some((prefix) => spec.startsWith(prefix))),
  );
}

/** One site's share: the spec files it names and their seconds. */
export interface SiteShare {
  specs: string[];
  seconds: number;
}

/**
 * Shares the known spec files out over `count` sites, heaviest first, each to the lightest site so far (the first of
 * equals). The same file gives the same plan in every process, so the sites agree without talking. The last site's own
 * files are only its weight: it runs every spec file the others do not name.
 */
export function planSites(times: SpecTimes, count: number): SiteShare[] {
  const shares: SiteShare[] = Array.from({ length: count }, () => ({ specs: [], seconds: 0 }));
  const specs = Object.entries(times).sort(([a, x], [b, y]) => y - x || (a < b ? -1 : a > b ? 1 : 0));
  for (const [spec, seconds] of specs) {
    let lightest = shares[0]!;
    for (const share of shares) if (share.seconds < lightest.seconds) lightest = share;
    lightest.specs.push(spec);
    lightest.seconds += seconds;
  }
  for (const share of shares) share.specs.sort();
  return shares;
}

const escapeRegExp = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** A pattern that matches one spec file's absolute path, with either separator. */
export function specPattern(spec: string): RegExp {
  const parts = spec.replace(/\\/g, '/').split('/').map(escapeRegExp);
  return new RegExp(`(?:^|[\\\\/])${parts.join('[\\\\/]')}$`);
}

/**
 * What a project of the config matches on a site: a site before the last its own spec files, the last site every spec
 * file except those of the others. Nothing changes for a default run.
 */
export function siteMatch(
  plan: readonly SiteShare[],
  site: Site,
): { testMatch: RegExp[] } | { testIgnore: RegExp[] } | Record<string, never> {
  if (!isShared(site)) return {};
  if (site.index < site.count) return { testMatch: (plan[site.index - 1]?.specs ?? []).map(specPattern) };
  return { testIgnore: plan.slice(0, -1).flatMap((share) => share.specs.map(specPattern)) };
}
