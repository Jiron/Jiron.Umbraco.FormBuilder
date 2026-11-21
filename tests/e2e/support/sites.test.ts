import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  formatSpecTimes,
  HOST_PORTS,
  isShared,
  MAX_SITES,
  ONE_SITE,
  planSites,
  portsOfSite,
  readSpecTimes,
  siteCredentialsKey,
  siteFromEnv,
  siteLabel,
  siteMatch,
  sitePort,
  SITE_PORT_TABLE,
  siteState,
  specPattern,
  STUB_PORTS,
  timesBelow,
  type Site,
  type SiteShare,
} from './sites.ts';
import { E2E_BASE_URL, E2E_PORT, e2eState, PORT_RANGE, repoRoot, STATIC_PORT, STUB_PORT } from './paths.ts';

const temp = mkdtempSync(join(tmpdir(), 'jfb-sites-'));
after(() => rmSync(temp, { recursive: true, force: true }));

const site = (index: number, count: number): Site => ({ index, count, budgets: false });
const BUDGETS_SITE: Site = { index: 1, count: 1, budgets: true };

describe('the site of a process', () => {
  test('is the one site of a default run when nothing is set', () => {
    assert.deepEqual(siteFromEnv({}), ONE_SITE);
    assert.equal(isShared(ONE_SITE), false);
  });

  test('is read from JFB_E2E_SITES and JFB_E2E_SITE, or is the budgets site', () => {
    assert.deepEqual(siteFromEnv({ JFB_E2E_SITES: '3', JFB_E2E_SITE: '2' }), site(2, 3));
    assert.deepEqual(siteFromEnv({ JFB_E2E_SITE: 'budgets' }), BUDGETS_SITE);
    assert.equal(isShared(site(2, 3)), true);
    assert.equal(isShared(BUDGETS_SITE), false);
  });

  test('refuses a site beyond the count, more sites than the ports allow, and nonsense', () => {
    assert.throws(() => siteFromEnv({ JFB_E2E_SITES: '2', JFB_E2E_SITE: '3' }), /from 1 to 2/);
    assert.throws(() => siteFromEnv({ JFB_E2E_SITES: String(MAX_SITES + 1) }), /from 1 to 3/);
    assert.throws(() => siteFromEnv({ JFB_E2E_SITES: '0' }), /JFB_E2E_SITES/);
    assert.throws(() => siteFromEnv({ JFB_E2E_SITES: 'two' }), /JFB_E2E_SITES/);
    assert.throws(() => siteFromEnv({ JFB_E2E_SITE: '1.5' }), /JFB_E2E_SITE /);
    assert.throws(() => siteFromEnv({ JFB_E2E_SITES: '2', JFB_E2E_SITE: 'budgets' }), /leave JFB_E2E_SITES out/);
  });

  test("keeps its state, credentials and log name apart from every other site's", () => {
    assert.equal(siteState('R', ONE_SITE), join('R', '.local', 'e2e'));
    assert.equal(siteState('R', site(1, 2)), join('R', '.local', 'e2e', 'site-1'));
    assert.equal(siteState('R', site(3, 3)), join('R', '.local', 'e2e', 'site-3'));
    assert.equal(siteState('R', BUDGETS_SITE), join('R', '.local', 'e2e', 'budgets'));
    assert.equal(siteCredentialsKey(ONE_SITE), 'e2e');
    assert.equal(siteCredentialsKey(site(2, 3)), 'e2e-site-2');
    assert.equal(siteCredentialsKey(BUDGETS_SITE), 'e2e-budgets');
    assert.equal(siteLabel(site(2, 3)), 'site 2/3');
    assert.equal(siteLabel(BUDGETS_SITE), 'budgets');
  });

  test('of this process, with nothing set, is the run as it always was', () => {
    // The support tests run without JFB_E2E_SITES or JFB_E2E_SITE.
    assert.equal(E2E_PORT, 5520);
    assert.equal(E2E_BASE_URL, 'http://127.0.0.1:5520');
    assert.equal(STATIC_PORT, 5521);
    assert.equal(STUB_PORT, 5560);
    assert.equal(e2eState, join(repoRoot, '.local', 'e2e'));
  });
});

describe("the sites' ports", () => {
  const all = Array.from({ length: MAX_SITES }, (_, i) => portsOfSite(i + 1));

  test('site 1 keeps the ports the harness always had', () => {
    assert.deepEqual(
      portsOfSite(1),
      [5520, 5521, 5525, 5526, 5527, 5528, 5529, 5531, 5532, 5533, 5534, 5535, 5536, 5537, 5538, 5560, 5561],
    );
  });

  test('every server has a port on every site, and no two sites, or servers, share one', () => {
    for (const ports of Object.values(SITE_PORT_TABLE)) assert.equal(ports.length, MAX_SITES);
    const flat = all.flat();
    assert.equal(new Set(flat).size, flat.length, `a port is given twice: ${flat.join(', ')}`);
  });

  test("are all inside Jiron Form Builder's range, never the demo's own port", () => {
    for (const port of all.flat()) {
      assert.ok(port >= PORT_RANGE.first && port <= PORT_RANGE.last, `${port} is in the range`);
      assert.notEqual(port, 5500);
    }
  });

  test('of a site are what its processes use; the budgets site uses site 1s', () => {
    assert.equal(sitePort(HOST_PORTS, site(2, 3)), 5562);
    assert.equal(sitePort(STUB_PORTS, site(3, 3)), 5595);
    assert.equal(sitePort(HOST_PORTS, BUDGETS_SITE), 5520);
  });

  /** The ports and ranges a row of ROADMAP section 4.11's table names in its first cell. */
  function roadmapRows(): { use: string; ports: number[] }[] {
    const roadmap = readFileSync(join(repoRoot, 'ROADMAP.md'), 'utf8');
    const section = roadmap.slice(roadmap.indexOf('### 4.11 '), roadmap.indexOf('### 4.12 '));
    return [...section.matchAll(/^\| (\d{4})(?: to (\d{4}))? \| (.+) \|$/gm)].map(([, first, last, use]) => {
      const from = Number(first);
      const to = Number(last ?? first);
      return { use: use ?? '', ports: Array.from({ length: to - from + 1 }, (_, i) => from + i) };
    });
  }

  test("of sites 2 and 3 are taken by nothing else in ROADMAP section 4.11's table, which gives them a row", () => {
    const rows = roadmapRows();
    const ours = rows.filter((row) => row.use.startsWith("The browser tests' second and third sites"));
    assert.equal(ours.length, 1, 'the table gives the sites a row');
    const others = new Set(rows.filter((row) => !ours.includes(row)).flatMap((row) => row.ports));
    for (const port of all.slice(1).flat()) {
      assert.ok(ours[0]!.ports.includes(port), `${port} is inside the sites' row`);
      assert.ok(!others.has(port), `${port} is in another row of the table`);
    }
  });

  /** The files below a folder whose names end with one of the endings. */
  function filesBelow(dir: string, endings: string[]): string[] {
    return readdirSync(dir).flatMap((name) => {
      const path = join(dir, name);
      if (name === 'node_modules' || name === 'bin' || name === 'obj') return [];
      if (statSync(path).isDirectory()) return filesBelow(path, endings);
      return endings.some((ending) => name.endsWith(ending)) ? [path] : [];
    });
  }

  test('of sites 2 and 3 are named by no other script, workflow or setting', () => {
    const files = [
      ...filesBelow(join(repoRoot, 'scripts'), ['.mjs', '.ps1']),
      ...filesBelow(join(repoRoot, '.github'), ['.yml']),
      ...filesBelow(join(repoRoot, 'tests', 'e2e', 'load'), ['.ts']),
      ...filesBelow(join(repoRoot, 'tests', 'e2e', 'install'), ['.ts']),
      join(repoRoot, 'demo', 'Jiron.Umbraco.FormBuilder.Demo', 'appsettings.json'),
    ];
    assert.ok(files.length > 30, 'the scripts are found');
    const ours = new Set(all.slice(1).flat());
    const named = files.flatMap((file) =>
      [...readFileSync(file, 'utf8').matchAll(/\b(55\d\d)\b/g)]
        .map(([, port]) => Number(port))
        .filter((port) => ours.has(port))
        .map((port) => `${port} in ${file}`),
    );
    assert.deepEqual(named, []);
  });
});

describe("the spec files' shares", () => {
  const times = {
    'specs/backoffice/places.spec.ts': 400,
    'specs/backoffice/library.spec.ts': 220,
    'specs/website/keyboard-pass.spec.ts': 260,
    'specs/website/themes.spec.ts': 30,
    'specs/smoke/site.spec.ts': 1,
    'specs/site-wide/settings.spec.ts': 80,
  };
  const absolute = (spec: string, separator = '\\'): string =>
    ['C:', 'Code', 'repo', 'tests', 'e2e', ...spec.split('/')].join(separator);

  /** Whether a project with a site's match runs a spec file, as Playwright decides it. */
  function runs(match: ReturnType<typeof siteMatch>, file: string): boolean {
    if ('testMatch' in match) return match.testMatch.some((pattern) => pattern.test(file));
    if ('testIgnore' in match) return !match.testIgnore.some((pattern) => pattern.test(file));
    return true;
  }

  test('weigh each spec file once, heaviest first to the lightest site', () => {
    const plan = planSites(times, 3);
    assert.deepEqual(
      plan.map((share) => share.specs),
      [
        ['specs/backoffice/places.spec.ts'],
        ['specs/smoke/site.spec.ts', 'specs/website/keyboard-pass.spec.ts', 'specs/website/themes.spec.ts'],
        ['specs/backoffice/library.spec.ts', 'specs/site-wide/settings.spec.ts'],
      ],
    );
    assert.deepEqual(
      plan.map((share) => share.seconds),
      [400, 291, 300],
    );
    assert.deepEqual(planSites(times, 3), plan, 'the same file gives the same plan in every process');
  });

  test('give every known spec file to exactly one site, with either separator', () => {
    for (const count of [2, 3]) {
      const plan = planSites(times, count);
      for (const spec of Object.keys(times)) {
        for (const separator of ['\\', '/']) {
          const on = Array.from({ length: count }, (_, i) => i + 1).filter((index) =>
            runs(siteMatch(plan, site(index, count)), absolute(spec, separator)),
          );
          assert.equal(on.length, 1, `${spec} runs on site(s) ${on.join(', ')} of ${count}`);
        }
      }
    }
  });

  test('run a spec file that no site names on the last site only', () => {
    const plan = planSites(times, 3);
    const unknown = absolute('specs/website/new-thing.spec.ts');
    assert.equal(runs(siteMatch(plan, site(1, 3)), unknown), false);
    assert.equal(runs(siteMatch(plan, site(2, 3)), unknown), false);
    assert.equal(runs(siteMatch(plan, site(3, 3)), unknown), true);
  });

  test('change nothing for the one site of a default run, nor for the budgets site', () => {
    const plan = planSites(times, 1);
    assert.deepEqual(siteMatch(plan, ONE_SITE), {});
    assert.deepEqual(siteMatch(plan, BUDGETS_SITE), {});
  });

  test('match the whole file name only', () => {
    const pattern = specPattern('specs/website/site.spec.ts');
    assert.equal(pattern.test(absolute('specs/website/site.spec.ts')), true);
    assert.equal(pattern.test(absolute('specs/website/multi-site.spec.ts')), false);
    assert.equal(pattern.test(absolute('specs/website/site.spec.ts.bak')), false);
    assert.equal(pattern.test(absolute('specs/websiteXsite.spec.ts')), false);
  });

  test("a site before the last whose share is empty runs nothing; the last runs all the others don't name", () => {
    const plan: SiteShare[] = [
      { specs: [], seconds: 0 },
      { specs: ['specs/smoke/site.spec.ts'], seconds: 1 },
    ];
    assert.equal(runs(siteMatch(plan, site(1, 2)), absolute('specs/smoke/site.spec.ts')), false);
    assert.equal(runs(siteMatch(plan, site(2, 2)), absolute('specs/smoke/site.spec.ts')), true);
  });

  test("are weighed by the spec files of the run's projects only", () => {
    const below = timesBelow({ ...times, 'specs/ux/walk.spec.ts': 900 }, ['specs/backoffice', 'specs/smoke/']);
    assert.deepEqual(Object.keys(below).sort(), [
      'specs/backoffice/library.spec.ts',
      'specs/backoffice/places.spec.ts',
      'specs/smoke/site.spec.ts',
    ]);
  });

  test('are read from the times file and written back sorted, to a tenth of a second', () => {
    const text = formatSpecTimes({ 'specs/b.spec.ts': 2.04, 'specs/a.spec.ts': 0.01 }, 3);
    assert.deepEqual(readSpecTimes(text), { 'specs/a.spec.ts': 0.1, 'specs/b.spec.ts': 2 });
    assert.match(text, /on 3 site\(s\)/);
    assert.deepEqual(readSpecTimes(null), {});
    assert.throws(() => readSpecTimes('{}'), /no "specs" object/);
    assert.throws(() => readSpecTimes('{"specs":{"specs/a.spec.ts":"x"}}'), /no number of seconds/);
  });

  test('the tracked times file is valid and names only spec files that exist', () => {
    const e2e = join(repoRoot, 'tests', 'e2e');
    const known = readSpecTimes(readFileSync(join(e2e, 'spec-times.json'), 'utf8'));
    assert.ok(Object.keys(known).length > 50);
    for (const spec of Object.keys(known)) {
      assert.ok(statSync(join(e2e, spec), { throwIfNoEntry: false })?.isFile(), `${spec} exists`);
      assert.ok(!spec.startsWith('specs/budgets/'), 'budgets runs on no site, so it is not weighed');
    }
  });
});

describe('the credentials file', () => {
  test("keeps every site's entry when the sites write it at the same moment", async () => {
    const file = join(temp, 'credentials.json');
    const users = join(import.meta.dirname, 'users.ts').replaceAll('\\', '/');
    const writer = (key: string) =>
      new Promise<number | null>((resolve) => {
        const script = [
          `import { updateCredentials } from 'file:///${users.replace(/^\//, '')}';`,
          `for (let i = 0; i < 25; i++) updateCredentials((all) => ({ ...all, ['${key}']: { round: i } }), process.argv[1]);`,
        ].join('\n');
        const child = spawn(process.execPath, ['--input-type=module', '-e', script, file], { stdio: 'ignore' });
        child.on('close', resolve);
      });
    const codes = await Promise.all(['e2e-site-1', 'e2e-site-2', 'e2e-site-3'].map(writer));
    assert.deepEqual(codes, [0, 0, 0]);
    const written = JSON.parse(readFileSync(file, 'utf8')) as Record<string, { round: number }>;
    assert.deepEqual(Object.keys(written).sort(), ['e2e-site-1', 'e2e-site-2', 'e2e-site-3']);
    for (const entry of Object.values(written)) assert.equal(entry.round, 24);
    assert.deepEqual(
      readdirSync(temp).filter((name) => name !== 'credentials.json'),
      [],
      'no lock or temporary file is left',
    );
  });
});
