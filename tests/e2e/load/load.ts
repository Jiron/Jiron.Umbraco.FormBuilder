// The load test (ROADMAP 16.2, ADR-0011, ADR-0238): a host of its own on 5523 with a fresh SQLite database, seeded through
// the test endpoints (support: LoadEndpoints), and every server budget of section 4.12 measured against it at a stated
// concurrency, one part after the other, so no part shares the machine with another:
//
//   - rendering the fifty-field form from the cached definition, one at a time and ten at a time, and a page with a form;
//   - a submission (store plus outbox), ten arriving at the same moment;
//   - where SQLite stops keeping up: bursts of 10 to 160 submissions arriving at the same moment;
//   - the first mail after a submission;
//   - 200 submissions a minute with an upload each, for JFB_LOAD_MINUTES minutes (3);
//   - JFB_LOAD_ENTRIES entries (100,000) seeded through the submission pipeline, and the entries list over them: its first
//     page, a state, one field, two fields, a search, a sort by a value, a later page by its cursor, and the count;
//   - a 10,000-entry CSV export, streamed;
//   - a folder of 1,000 forms in the tree, through the API and drawn in the browser;
//   - Lighthouse on a page with forms and on the same layout without one, three runs each, simulated and applied to a
//     real load (Lighthouse from %TEMP%\jfb-lighthouse).
//
// It writes .local/load/results.json and prints a table. `JFB_E2E_SKIP_BUILD=1` reuses the current build, and
// `JFB_LOAD_ONLY` runs some parts (render, submit, sqlite, mail, sustained, entries, export, tree, lighthouse).
//
//   node tests/e2e/load/load.ts        (npm run load in tests/e2e)

import { chromium, type APIRequestContext } from '@playwright/test';
import { execSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { performance } from 'node:perf_hooks';
import { setTimeout as sleep } from 'node:timers/promises';
import { loginThroughApi } from '../support/api-login.ts';
import { formPages, pathOf } from '../support/demo-content.ts';
import { describeStall, waitForStartupWork } from '../support/host-health.ts';
import { buildDemo, ensureClientAssets, logTail, startHost, type Host } from '../support/host.ts';
import { hostEnvironment } from '../support/host-env.ts';
import { LOAD_BASE_URL, LOAD_PORT, repoRoot } from '../support/paths.ts';
import { treeRow } from '../support/tree.ts';
import { createSecrets, type E2ESecrets } from '../support/users.ts';

const PUBLIC = `${LOAD_BASE_URL}/jfb/api/v1`;
const MANAGEMENT = '/umbraco/jfb/management/api/v1';
const ENTRIES = Number(process.env['JFB_LOAD_ENTRIES'] ?? '100000');
const MINUTES = Number(process.env['JFB_LOAD_MINUTES'] ?? '3');
const SEED_CHUNK = 10_000;

/** The parts to run, as `JFB_LOAD_ONLY=render,submit` names them; all of them by default. */
const PARTS = new Set(
  (process.env['JFB_LOAD_ONLY'] ?? 'render,submit,sqlite,mail,sustained,entries,export,tree,lighthouse')
    .split(',')
    .map((part) => part.trim()),
);

/**
 * Lighthouse, run from a throwaway folder of its own and never one of the repository's packages (ADR-0238): 12.8.2 is the
 * newest whose tree holds only licences rule 4 allows (from 13 on, @sentry/node brings a source-available package), and its
 * tree's advisories would otherwise sit in the browser tests' lock, though it runs only against this machine's load host.
 */
const LIGHTHOUSE_DIR = join(process.env['TEMP'] ?? tmpdir(), 'jfb-lighthouse');
const LIGHTHOUSE_PACKAGES = ['lighthouse@12.8.2', 'chrome-launcher@1.2.0'];
const OUT = join(repoRoot, '.local', 'load');

/** One measured part: what, at which concurrency, its numbers in milliseconds, the budget and whether it is met. */
interface Measured {
  readonly part: string;
  readonly concurrency: string;
  readonly samples: number;
  readonly median: number;
  readonly p95: number;
  readonly max: number;
  readonly budget?: string;
  readonly met?: boolean;
  readonly note?: string;
}

interface LoadForm {
  readonly key: string;
  readonly revision: number;
  readonly fields: Record<string, string>;
}

interface LoadForms {
  readonly entries: LoadForm;
  readonly wide: LoadForm;
  readonly upload: LoadForm;
}

const results: Measured[] = [];
const notes: Record<string, unknown> = {};

/** The value at a fraction of the sorted samples, by nearest rank. */
function at(sorted: readonly number[], fraction: number): number {
  if (sorted.length === 0) return Number.NaN;
  return sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(fraction * sorted.length) - 1))] ?? Number.NaN;
}

function record(
  part: string,
  concurrency: string,
  samples: readonly number[],
  budget?: { text: string; p95?: number; max?: number },
  note?: string,
): Measured {
  const sorted = [...samples].sort((a, b) => a - b);
  const measured: Measured = {
    part,
    concurrency,
    samples: sorted.length,
    median: round(at(sorted, 0.5)),
    p95: round(at(sorted, 0.95)),
    max: round(sorted.at(-1) ?? Number.NaN),
    ...(budget === undefined
      ? {}
      : {
          budget: budget.text,
          met:
            (budget.p95 === undefined || at(sorted, 0.95) <= budget.p95) &&
            (budget.max === undefined || (sorted.at(-1) ?? Infinity) <= budget.max),
        }),
    ...(note === undefined ? {} : { note }),
  };
  results.push(measured);
  console.log(
    `load: ${part} (${concurrency}): median ${measured.median} ms, p95 ${measured.p95} ms, max ${measured.max} ms${budget ? `, budget ${budget.text}: ${measured.met ? 'met' : 'MISSED'}` : ''}${note ? ` (${note})` : ''}`,
  );
  return measured;
}

const round = (value: number): number => Math.round(value * 10) / 10;

/** Times a call, its whole answer read. */
async function timed<T>(call: () => Promise<T>): Promise<{ ms: number; value: T }> {
  const started = performance.now();
  const value = await call();
  return { ms: performance.now() - started, value };
}

async function json<T>(response: Response): Promise<T> {
  const text = await response.text();
  if (!response.ok) throw new Error(`${response.url} answered ${response.status}: ${text.slice(0, 300)}`);
  return JSON.parse(text) as T;
}

async function post<T>(path: string, body: unknown): Promise<T> {
  return json<T>(
    await fetch(`${LOAD_BASE_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(30 * 60_000),
    }),
  );
}

/** A small PDF, as a visitor's plan would be. */
const PLAN = Buffer.from(
  '%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n' +
    '3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >> endobj\n' +
    `% ${'plan '.repeat(8000)}\ntrailer << /Root 1 0 R >>\n%%EOF\n`,
);

/** A visitor's challenge, as the page asks for it when the form is first touched, and when it was issued. */
async function challenge(form: string): Promise<{ value: string; issued: number }> {
  const answer = await json<{ challenge: string }>(
    await fetch(`${PUBLIC}/forms/${form}/challenge`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    }),
  );
  return { value: answer.challenge, issued: Date.now() };
}

/** Waits until a challenge counts as filled in by a person (FormChallenges.MinFillSeconds, three seconds). */
async function filled(issued: number): Promise<void> {
  const wait = issued + 3_200 - Date.now();
  if (wait > 0) await sleep(wait);
}

/** Uploads the plan with the challenge, and answers its token. */
async function upload(form: LoadForm, challengeValue: string): Promise<{ ms: number; token: string }> {
  const body = new FormData();
  body.append('challenge', challengeValue);
  body.append('file', new Blob([PLAN], { type: 'application/pdf' }), 'plan.pdf');
  const { ms, value } = await timed(async () =>
    json<{ token: string }>(
      await fetch(`${PUBLIC}/forms/${form.key}/uploads?fieldKey=${form.fields['plan'] ?? ''}`, {
        method: 'POST',
        body,
      }),
    ),
  );
  return { ms, token: value.token };
}

/** Sends the upload form as a visitor's page does, and answers how long the send took. */
async function send(form: LoadForm, reference: string, challengeValue: string, token?: string): Promise<number> {
  const values: Record<string, string[]> = {
    name: [`Visitor ${reference}`],
    email: [`${reference.toLowerCase()}@visitor.load.jfb.test`],
    reference: [reference],
  };
  if (token !== undefined) values['plan'] = [token];
  const { ms } = await timed(async () =>
    json<unknown>(
      await fetch(`${PUBLIC}/forms/${form.key}/entries`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          submissionId: randomUUID(),
          revision: form.revision,
          culture: 'en-US',
          values,
          challenge: challengeValue,
        }),
      }),
    ),
  );
  return ms;
}

/** When the mail that names a reference reached the pickup folder, by its file's time; undefined while it has not. */
function mailTimes(mailDir: string): Map<string, number> {
  const times = new Map<string, number>();
  if (!existsSync(mailDir)) return times;
  for (const file of readdirSync(mailDir).filter((name) => name.endsWith('.eml'))) {
    const path = join(mailDir, file);
    const subject = /^Subject: Load (\S+)/m.exec(readFileSync(path, 'utf8'))?.[1];
    if (subject !== undefined) times.set(subject, statSync(path).mtimeMs);
  }
  return times;
}

async function measureRendering(forms: LoadForms): Promise<void> {
  const url = `${PUBLIC}/forms/${forms.wide.key}/render?instance=jfb-1&page=/load/&culture=en-US`;
  for (let warm = 0; warm < 20; warm++) await (await fetch(url)).text();
  const one: number[] = [];
  for (let i = 0; i < 300; i++) one.push((await timed(async () => (await fetch(url)).text())).ms);
  record('Rendering the 50-field form (render endpoint)', '1 at a time', one, { text: '\u2264 20 ms', p95: 20 });

  const ten: number[] = [];
  const started = performance.now();
  await Promise.all(
    Array.from({ length: 10 }, async () => {
      for (let i = 0; i < 60; i++) ten.push((await timed(async () => (await fetch(url)).text())).ms);
    }),
  );
  const seconds = (performance.now() - started) / 1000;
  record(
    'Rendering the 50-field form (render endpoint)',
    '10 at a time',
    ten,
    undefined,
    `${Math.round(ten.length / seconds)} renders a second`,
  );

  const contact = formPages().find((page) => page.form === 'contact');
  if (contact !== undefined) {
    const page = `${LOAD_BASE_URL}${pathOf(contact.texts.en, 'en')}`;
    for (let warm = 0; warm < 10; warm++) await (await fetch(page)).text();
    const pages: number[] = [];
    for (let i = 0; i < 100; i++) pages.push((await timed(async () => (await fetch(page)).text())).ms);
    record("A whole page with a form (the demo's Contact)", '1 at a time', pages);
  }
}

async function measureSubmissions(forms: LoadForms): Promise<void> {
  // Ten at the same moment, ten times: each with its challenge filled in, sent together. A round before them, not
  // counted, warms what a server's first sends meet once (the pipeline's code and statements), as rendering is warmed.
  const sends: number[] = [];
  for (let round = -1; round < 10; round++) {
    const challenges = await Promise.all(Array.from({ length: 10 }, () => challenge(forms.upload.key)));
    await filled(Math.max(...challenges.map((each) => each.issued)));
    const took = await Promise.all(
      challenges.map((each, index) => send(forms.upload, `B${round}x${index}`, each.value)),
    );
    if (round >= 0) sends.push(...took);
  }
  record('A submission (store plus outbox)', '10 at the same moment', sends, {
    text: '\u2264 150 ms at p95',
    p95: 150,
  });
}

/**
 * Where SQLite stops keeping up (section 4.12): bursts of growing size, every send of a burst arriving at the same
 * moment, five bursts of each size. SQLite has one writer, which Umbraco shares, so the stores queue there; the size at
 * which a burst's p95 first passes the submission budget is where a site should move to SQL Server.
 */
async function measureBursts(forms: LoadForms): Promise<void> {
  let first: number | undefined;
  for (const size of [10, 20, 40, 80, 160]) {
    const sends: number[] = [];
    let wall = 0;
    for (let round = 0; round < 5; round++) {
      const challenges = await Promise.all(Array.from({ length: size }, () => challenge(forms.upload.key)));
      await filled(Math.max(...challenges.map((each) => each.issued)));
      const started = performance.now();
      sends.push(
        ...(await Promise.all(
          challenges.map((each, index) => send(forms.upload, `S${size}r${round}x${index}`, each.value)),
        )),
      );
      wall += performance.now() - started;
    }
    const measured = record(
      'A submission in a burst',
      `${size} at the same moment`,
      sends,
      undefined,
      `${Math.round((size * 5) / (wall / 1000))} stored a second`,
    );
    if (first === undefined && measured.p95 > 150) first = size;
  }
  notes['sqlite'] =
    first === undefined
      ? 'every burst up to 160 at the same moment kept its p95 within 150 ms'
      : `a burst's p95 first passed 150 ms with ${first} at the same moment`;
  console.log(`load: SQLite: ${String(notes['sqlite'])}`);
}

async function measureFirstMail(forms: LoadForms, host: Host): Promise<void> {
  const times: number[] = [];
  for (let i = 0; i < 5; i++) {
    const reference = `M${i}`;
    const asked = await challenge(forms.upload.key);
    await filled(asked.issued);
    await send(forms.upload, reference, asked.value);
    const sent = Date.now();
    const deadline = sent + 60_000;
    let arrived: number | undefined;
    while (arrived === undefined && Date.now() < deadline) {
      arrived = mailTimes(host.mailDir).get(reference);
      if (arrived === undefined) await sleep(100);
    }
    times.push(arrived === undefined ? Number.POSITIVE_INFINITY : arrived - sent);
  }
  record('The first mail after a submission', 'one submission at a time', times, { text: '\u2264 10 s', max: 10_000 });
}

async function measureSustained(forms: LoadForms, host: Host): Promise<void> {
  // 200 a minute, one every 300 ms, each a visitor: a challenge, three seconds, the upload, the send.
  const uploads: number[] = [];
  const sends: number[] = [];
  const sentAt = new Map<string, number>();
  let failures = 0;
  const visitors: Promise<void>[] = [];
  const count = Math.round(MINUTES * 200);
  const started = Date.now();
  for (let i = 0; i < count; i++) {
    const due = started + i * 300;
    if (due > Date.now()) await sleep(due - Date.now());
    const reference = `S${i}`;
    visitors.push(
      (async () => {
        try {
          const asked = await challenge(forms.upload.key);
          await filled(asked.issued);
          const uploaded = await upload(forms.upload, asked.value);
          uploads.push(uploaded.ms);
          sends.push(await send(forms.upload, reference, asked.value, uploaded.token));
          sentAt.set(reference, Date.now());
        } catch (error) {
          failures++;
          if (failures <= 3)
            console.warn(`load: a visitor failed: ${error instanceof Error ? error.message : String(error)}`);
        }
      })(),
    );
  }
  await Promise.all(visitors);
  const minutes = (Date.now() - started) / 60_000;
  const note = `${sends.length} sent in ${minutes.toFixed(1)} minutes, ${failures} failed`;
  record('An upload (200 submissions a minute)', '200 a minute', uploads, undefined, note);
  record(
    'A submission with its upload (200 a minute)',
    '200 a minute',
    sends,
    { text: '\u2264 150 ms at p95', p95: 150 },
    note,
  );
  notes['sustainedFailures'] = failures;

  // Every mail of the run, by when it reached the pickup folder after its send.
  const deadline = Date.now() + 120_000;
  let mails = mailTimes(host.mailDir);
  while ([...sentAt.keys()].some((reference) => !mails.has(reference)) && Date.now() < deadline) {
    await sleep(500);
    mails = mailTimes(host.mailDir);
  }
  const delays = [...sentAt].map(([reference, at]) => (mails.get(reference) ?? Number.POSITIVE_INFINITY) - at);
  record('The mail after each submission (200 a minute)', '200 a minute', delays, { text: '\u2264 10 s', p95: 10_000 });
}

async function seed(forms: LoadForms): Promise<void> {
  const started = performance.now();
  for (let from = 0; from < ENTRIES; from += SEED_CHUNK) {
    const chunk = await post<{ sent: number; milliseconds: number }>('/jfb-testing/load-entries', {
      form: forms.entries.key,
      from,
      count: Math.min(SEED_CHUNK, ENTRIES - from),
    });
    console.log(
      `load: seeded ${from + chunk.sent} of ${ENTRIES} entries (${Math.round(chunk.sent / (chunk.milliseconds / 1000))} a second)`,
    );
  }
  const seconds = (performance.now() - started) / 1000;
  notes['seeding'] = { entries: ENTRIES, seconds: Math.round(seconds), perSecond: Math.round(ENTRIES / seconds) };
}

async function measureEntries(api: APIRequestContext, forms: LoadForms): Promise<void> {
  const list = `${MANAGEMENT}/form/${forms.entries.key}/entries`;
  const field = (alias: string): string => forms.entries.fields[alias] ?? '';
  const filter = (rest: Record<string, unknown>) => ({ states: [], fields: [], forms: [], ...rest });
  const shapes: { name: string; body: Record<string, unknown> }[] = [
    { name: 'its first page, newest first', body: { filter: filter({}), count: false } },
    { name: 'its first page with the count', body: { filter: filter({}), count: true } },
    { name: 'one state', body: { filter: filter({ states: ['submitted'] }), count: false } },
    {
      name: 'one field',
      body: { filter: filter({ fields: [{ field: field('position'), test: 'oneOf', values: ['o3'] }] }), count: false },
    },
    {
      name: 'two fields',
      body: {
        filter: filter({
          fields: [
            { field: field('position'), test: 'oneOf', values: ['o3'] },
            { field: field('country'), test: 'oneOf', values: ['o5'] },
          ],
        }),
        count: false,
      },
    },
    { name: 'a search', body: { filter: filter({ search: 'Person 004200' }), count: false } },
    {
      name: 'sorted by a number',
      body: { filter: filter({ sort: { field: field('amount'), descending: true } }), count: false },
    },
  ];
  for (const shape of shapes) {
    const times: number[] = [];
    for (let run = 0; run < 10; run++) {
      const { ms, value } = await timed(async () => {
        const response = await api.post(list, { data: shape.body });
        const text = await response.text();
        if (!response.ok())
          throw new Error(`The entries list (${shape.name}) answered ${response.status()}: ${text.slice(0, 300)}`);
        return text;
      });
      if (run > 0) times.push(ms);
      else if (!value.includes('"items"')) throw new Error(`The entries list (${shape.name}) answered no items.`);
    }
    record(`The entries list over ${ENTRIES.toLocaleString('en')} entries: ${shape.name}`, '1 at a time', times, {
      text: '\u2264 300 ms',
      max: 300,
    });
  }

  // A later page by its cursor: twenty pages in, as a user who reads on.
  let after: string | null | undefined;
  const pages: number[] = [];
  for (let page = 0; page < 20; page++) {
    const { ms, value } = await timed(
      async () =>
        (await api.post(list, { data: { filter: filter({}), count: false, after } })).json() as Promise<{
          next?: string | null;
        }>,
    );
    if (page > 0) pages.push(ms);
    after = value.next;
  }
  record(
    `The entries list over ${ENTRIES.toLocaleString('en')} entries: the next 19 pages by their cursor`,
    '1 at a time',
    pages,
    { text: '\u2264 300 ms', max: 300 },
  );
}

async function measureExport(api: APIRequestContext, forms: LoadForms): Promise<void> {
  const body = {
    format: 'csv',
    filter: {
      states: [],
      fields: [{ field: forms.entries.fields['position'] ?? '', test: 'oneOf', values: ['o3'] }],
      forms: [],
    },
    sensitive: false,
    remember: false,
  };
  const times: number[] = [];
  let lines = 0;
  for (let run = 0; run < 3; run++) {
    const { ms, value } = await timed(async () => {
      const response = await api.post(`${MANAGEMENT}/form/${forms.entries.key}/entries/export/file`, {
        data: body,
        timeout: 120_000,
      });
      const text = await response.text();
      if (!response.ok()) throw new Error(`The export answered ${response.status()}: ${text.slice(0, 300)}`);
      return text;
    });
    times.push(ms);
    lines = value.split('\n').filter((line) => line.length > 0).length;
  }
  record(
    'A CSV export of 10,000 entries, streamed',
    '1 at a time',
    times,
    { text: '\u2264 5 s', max: 5_000 },
    `${lines - 1} rows`,
  );
}

async function measureTree(api: APIRequestContext, secrets: E2ESecrets): Promise<void> {
  const name = `Load tree ${randomUUID().slice(0, 8)}`;
  const tree = await post<{ folder: string; name: string; milliseconds: number }>('/jfb-testing/load-tree', {
    count: 1000,
    name,
  });
  const times: number[] = [];
  for (let run = 0; run < 10; run++) {
    const { ms } = await timed(async () => {
      const response = await api.get(`${MANAGEMENT}/tree/children?parentKey=${tree.folder}&skip=0&take=100`);
      if (!response.ok()) throw new Error(`The tree answered ${response.status()}.`);
      return response.text();
    });
    if (run > 0) times.push(ms);
  }
  record('The tree: a folder of 1,000 forms, its first 100 through the API', '1 at a time', times, {
    text: '< 1 s',
    max: 1_000,
  });

  // In the browser: from expanding the folder to its forms drawn.
  const browser = await chromium.launch({ channel: 'msedge' });
  try {
    const page = await browser.newPage({ baseURL: LOAD_BASE_URL, viewport: { width: 1440, height: 900 } });
    const admin = secrets.users.admin;
    await page.goto('/umbraco');
    await page.getByLabel(/email/i).first().fill(admin.email);
    await page
      .getByLabel(/password/i)
      .first()
      .fill(admin.password);
    await page
      .getByRole('button', { name: /^(login|log in|sign in)$/i })
      .first()
      .click();
    await page.locator('umb-backoffice-header').waitFor({ timeout: 60_000 });
    const drawn: number[] = [];
    try {
      for (let run = 0; run < 5; run++) {
        await page.goto('/umbraco/section/jfb-forms');
        const expand = page.getByRole('button', { name: `Expand child items for ${tree.name}`, exact: true });
        await expand.waitFor({ timeout: 60_000 });
        const started = performance.now();
        await expand.click();
        await treeRow(page, 'Load form 0001').first().waitFor({ timeout: 30_000 });
        drawn.push(performance.now() - started);
      }
    } catch (error) {
      mkdirSync(OUT, { recursive: true });
      await page.screenshot({ path: join(OUT, 'tree-failure.png') });
      throw error;
    }
    record('The tree: a folder of 1,000 forms opened in the browser', 'one user', drawn, { text: '< 1 s', max: 1_000 });
  } finally {
    await browser.close();
  }
}

/** What the load test reads of a Lighthouse result: each category's score and each audit's score and value. */
interface LighthouseResult {
  readonly categories: Record<string, { readonly score: number | null }>;
  readonly audits: Record<string, { readonly score: number | null; readonly numericValue?: number }>;
}

/** The metrics behind Lighthouse's performance score, by their audits' ids. */
const LIGHTHOUSE_METRICS = [
  'first-contentful-paint',
  'largest-contentful-paint',
  'total-blocking-time',
  'cumulative-layout-shift',
  'speed-index',
] as const;

async function measureLighthouse(): Promise<void> {
  let lighthouse:
    ((url: string, flags: Record<string, unknown>) => Promise<{ lhr: LighthouseResult } | undefined>) | undefined;
  let launch:
    ((options: Record<string, unknown>) => Promise<{ port: number; kill: () => Promise<void> | void }>) | undefined;
  try {
    const shim = join(LIGHTHOUSE_DIR, 'tools.mjs');
    if (!existsSync(join(LIGHTHOUSE_DIR, 'node_modules', 'lighthouse', 'package.json'))) {
      mkdirSync(LIGHTHOUSE_DIR, { recursive: true });
      writeFileSync(
        join(LIGHTHOUSE_DIR, 'package.json'),
        JSON.stringify({ name: 'jfb-lighthouse', private: true, type: 'module' }),
      );
      execSync(`npm install --no-save --no-audit --no-fund --ignore-scripts ${LIGHTHOUSE_PACKAGES.join(' ')}`, {
        cwd: LIGHTHOUSE_DIR,
        stdio: 'inherit',
      });
    }
    writeFileSync(
      shim,
      "export { default as lighthouse } from 'lighthouse';\nexport { launch } from 'chrome-launcher';\n",
    );
    const tools = (await import(pathToFileURL(shim).href)) as { lighthouse: typeof lighthouse; launch: typeof launch };
    lighthouse = tools.lighthouse;
    launch = tools.launch;
  } catch (error) {
    console.warn(
      `load: Lighthouse could not be set up in ${LIGHTHOUSE_DIR}, so its part is left out: ${error instanceof Error ? error.message : String(error)}`,
    );
    return;
  }
  if (lighthouse === undefined || launch === undefined) return;
  const edge = [
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  ].find((path) => existsSync(path));
  const contact = formPages().find((page) => page.form === 'contact');
  if (edge === undefined || contact === undefined) return;
  const pages = {
    // The Contact page holds its form and the layout's newsletter; the Privacy page, without the newsletter, holds none.
    'with forms (Contact, and the newsletter)': pathOf(contact.texts.en, 'en'),
    'without a form (Privacy, no newsletter)': `${pathOf({ name: 'Privacy', heading: 'Privacy' }, 'en')}?newsletter=off`,
  };
  const scores: Record<string, Record<string, number[]>> = {};
  const metrics: Record<string, Record<string, number[]>> = {};
  const missed: Record<string, Set<string>> = {};
  const browser = await launch({ chromePath: edge, chromeFlags: ['--headless=new', '--no-first-run'] });
  try {
    // Lighthouse's own simulation, which works out a slow phone's load from this machine's, and the same network and
    // processor applied to a real load in the browser, which also shows what the simulation's model leaves out.
    for (const [mode, run] of (['simulate', 'devtools'] as const).flatMap((each) =>
      [0, 1, 2].map((at) => [each, at] as const),
    ))
      for (const [page, path] of Object.entries(pages)) {
        const name = `${mode}: ${page}`;
        const result = await lighthouse(`${LOAD_BASE_URL}${path}`, {
          port: browser.port,
          output: 'json',
          logLevel: 'error',
          throttlingMethod: mode,
          onlyCategories: ['performance', 'accessibility', 'best-practices', 'seo'],
        });
        for (const [category, value] of Object.entries(result?.lhr.categories ?? {})) {
          ((scores[name] ??= {})[category] ??= []).push(Math.round((value.score ?? 0) * 100));
        }
        // The first run's whole report, kept beside the results for a closer look.
        if (run === 0 && result !== undefined) {
          mkdirSync(OUT, { recursive: true });
          writeFileSync(
            join(OUT, `lighthouse-${mode}-${page.split(' ')[0] ?? 'page'}.json`),
            JSON.stringify(result.lhr),
          );
        }
        for (const metric of LIGHTHOUSE_METRICS) {
          const value = result?.lhr.audits[metric]?.numericValue;
          if (value !== undefined) ((metrics[name] ??= {})[metric] ??= []).push(value);
        }
        // The metrics that kept a page from their full score, so a lower score says why.
        for (const metric of LIGHTHOUSE_METRICS) {
          const score = result?.lhr.audits[metric]?.score;
          if (score !== null && score !== undefined && score < 1) (missed[name] ??= new Set()).add(metric);
        }
      }
  } finally {
    await browser.kill();
  }
  const medians = Object.fromEntries(
    Object.entries(scores).map(([name, categories]) => [
      name,
      Object.fromEntries(
        Object.entries(categories).map(([category, values]) => [
          category,
          at(
            [...values].sort((a, b) => a - b),
            0.5,
          ),
        ]),
      ),
    ]),
  );
  const metricMedians = Object.fromEntries(
    Object.entries(metrics).map(([name, values]) => [
      name,
      Object.fromEntries(
        Object.entries(values).map(([metric, each]) => [
          metric,
          round(
            at(
              [...each].sort((a, b) => a - b),
              0.5,
            ),
          ),
        ]),
      ),
    ]),
  );
  notes['lighthouse'] = {
    scores: medians,
    metrics: metricMedians,
    short: Object.fromEntries(Object.entries(missed).map(([name, found]) => [name, [...found]])),
  };
  console.log(`load: Lighthouse medians of three runs each: ${JSON.stringify(notes['lighthouse'])}`);

  // The budget (section 4.12): the page with forms scores as the page without one, under the network and processor
  // applied to a real load. The simulation's scores are reported beside it: it counts the stylesheets a form brings into
  // the body as holding the first paint, served one after another on a local server's connection (ADR-0238).
  const [withForms, withoutForm] = Object.keys(pages).map((page) => medians[`devtools: ${page}`]?.['performance']);
  const met = withForms !== undefined && withoutForm !== undefined && withForms >= withoutForm;
  notes['lighthouseMet'] = met;
  console.log(
    `load: Lighthouse performance with forms ${String(withForms)}, without ${String(withoutForm)} (applied throttling), budget the same: ${met ? 'met' : 'MISSED'}`,
  );
  if (!met) process.exitCode = 1;
}

async function main(): Promise<void> {
  if (process.env['JFB_E2E_SKIP_BUILD'] !== '1') {
    ensureClientAssets();
    buildDemo();
  }
  const secrets = createSecrets();
  const env: Record<string, string> = {
    ...hostEnvironment(secrets, { deliveryApi: false }),
    // No stub on this host; the browser tests' run holds its port.
    JironFormBuilderTesting__StubPort: '0',
  };
  // One machine plays every visitor, so no limit of an address or a form may stand in its way.
  for (const policy of ['Read', 'Challenge', 'Submit', 'Upload'])
    env[`JironFormBuilder__Limits__${policy}__PerAddressAndFormPerMinute`] = '0';
  const host = await startHost({ name: `load-${LOAD_PORT}`, port: LOAD_PORT, env });
  try {
    const wait = await waitForStartupWork(host.logFile);
    if (wait.state === 'stalled') throw new Error(describeStall(wait.stall, host.logFile));
    if (wait.state !== 'done') throw new Error(`The load host's start-up work ended as "${wait.state}".`);
    const forms = await post<LoadForms>('/jfb-testing/load-forms', {});
    if (PARTS.has('render')) await measureRendering(forms);
    if (PARTS.has('submit')) await measureSubmissions(forms);
    if (PARTS.has('sqlite')) await measureBursts(forms);
    if (PARTS.has('mail')) await measureFirstMail(forms, host);
    if (PARTS.has('sustained')) await measureSustained(forms, host);
    if (PARTS.has('entries') || PARTS.has('export')) await seed(forms);
    if (PARTS.has('entries') || PARTS.has('export') || PARTS.has('tree')) {
      const api = await loginThroughApi(secrets.users.admin, LOAD_BASE_URL);
      try {
        if (PARTS.has('entries')) await measureEntries(api, forms);
        if (PARTS.has('export')) await measureExport(api, forms);
        if (PARTS.has('tree')) await measureTree(api, secrets);
      } finally {
        await api.dispose();
      }
    }
    if (PARTS.has('lighthouse')) await measureLighthouse();
  } catch (error) {
    console.error(
      `load: failed: ${error instanceof Error ? error.message : String(error)}\nLast log lines:\n${logTail(host.logFile)}`,
    );
    process.exitCode = 1;
  } finally {
    await host.stop();
  }

  mkdirSync(OUT, { recursive: true });
  const finished = new Date().toISOString();
  writeFileSync(
    join(OUT, 'results.json'),
    JSON.stringify({ at: finished, entries: ENTRIES, minutes: MINUTES, results, notes }, null, 2) + '\n',
  );
  console.log(
    '\n| Part | Concurrency | Samples | Median (ms) | p95 (ms) | Max (ms) | Budget | Met |\n| --- | --- | --- | --- | --- | --- | --- | --- |',
  );
  for (const each of results)
    console.log(
      `| ${each.part}${each.note ? ` (${each.note})` : ''} | ${each.concurrency} | ${each.samples} | ${each.median} | ${each.p95} | ${each.max} | ${each.budget ?? ''} | ${each.met === undefined ? '' : each.met ? 'yes' : 'no'} |`,
    );
  if (results.some((each) => each.met === false)) process.exitCode = 1;
}

await main();
