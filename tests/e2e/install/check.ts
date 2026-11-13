// The install test's checks through the site's own APIs and a browser (ROADMAP 16.5 and 18.3, ADR-0240, ADR-0248), run
// by scripts/install-test.ps1 against the site it installed. The admin comes from .local/credentials.json (installTest),
// written by the script, and signs in through Umbraco's own login and PKCE flow, and in the browser through Umbraco's
// sign-in page; nothing is printed of it.
//
//   node install/check.ts --url <site> --mode install --mail <pickup folder> --profile <browser profile folder>
//     the smoke (18.3): a form from the built-in contact template, given an email action to the team and published
//     through the management API; a template, a document type and a page that shows it, made and published through
//     Umbraco's own Management API. Then in Edge, with a browser profile kept in the given folder: the admin signs in
//     and sees the Form Builder section with the AI add-on's backoffice beside it, a visitor fills the form in on the
//     page and sends it, the team's mail lands in the pickup folder, and the entry shows in the form's Entries view;
//     the entries list and the entry's panel agree
//   node install/check.ts --url <site> --mode cache --profile <folder> --chunk <name> --old-chunk <name>
//     after the packages were updated (18.3, on the supported floor): the same browser profile, which keeps its cache
//     and its sign-in, opens the section again without a hard refresh, and the backoffice must run the new build: the
//     start chunk it loads is the new package's (--chunk), not the old one's (--old-chunk)
//   node install/check.ts --url <site> --mode upgrade --expected <baseline-data.json>
//     what the baseline stored (scripts/install-test/BaselineSeed), read back through today's APIs: the folders and
//     forms in the tree, the forms' versions and drafts, every entry's answers and state, the file's bytes, the
//     settings; then a visitor sends the upgraded form, and the entry takes the next number
//
// Exit codes: 0 every check passed, 1 a check failed (with what), 2 usage.

import { chromium, type APIRequestContext, type BrowserContext, type Page } from '@playwright/test';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { loginThroughApi } from '../support/api-login.ts';
import { headerOf, waitForMailIn } from '../support/mail.ts';
import { repoRoot } from '../support/paths.ts';

const MANAGEMENT = '/umbraco/jfb/management/api/v1';
const PUBLIC = '/jfb/api/v1';
const UMBRACO = '/umbraco/management/api/v1';
const SECTION = '/umbraco/section/jfb-forms';

/** Where the smoke's email action sends: the team, as an editor would set it in the Actions view. */
const TEAM = 'team@install.jfb.test';

/** The built-in contact template (4.5): a name, an email address and a message. */
const CONTACT_TEMPLATE = '66c2bf3e-fe43-41cc-8470-f446c2f01a89';

interface Expected {
  readonly settings: { readonly timeZone: string; readonly draftDays: number; readonly draftCount: number };
  readonly folders: readonly { readonly key: string; readonly name: string; readonly parentKey: string | null }[];
  readonly forms: readonly {
    readonly key: string;
    readonly alias: string;
    readonly name: string;
    readonly folderKey: string | null;
    readonly publishedVersions: number;
    readonly draftMessageLabel: string | null;
    readonly publishedMessageLabel: string | null;
  }[];
  readonly entries: readonly {
    readonly form: string;
    readonly key: string;
    readonly number: number;
    readonly state: string;
    readonly values: Readonly<Record<string, string>>;
  }[];
  readonly file: {
    readonly key: string;
    readonly name: string;
    readonly size: number;
    readonly sha256: string;
    readonly entry: string;
  };
}

interface Library {
  readonly items: readonly {
    readonly key: string;
    readonly kind: string;
    readonly name: string;
    readonly isPublished: boolean;
  }[];
}

interface Panel {
  readonly number: number;
  readonly state: string;
  readonly pages: readonly {
    readonly groups: readonly {
      readonly answers: readonly {
        readonly field: string;
        readonly texts: readonly string[];
        readonly files: readonly { readonly key: string; readonly name: string; readonly size: number }[];
      }[];
    }[];
  }[];
}

interface Field {
  readonly key: string;
  readonly alias?: string;
  readonly type: string;
  readonly label?: Readonly<Record<string, string>>;
}

interface Definition {
  readonly pages: readonly { readonly groups: readonly { readonly fields: readonly Field[] }[] }[];
}

function option(name: string): string | undefined {
  const at = process.argv.indexOf(name);
  return at >= 0 ? process.argv[at + 1] : undefined;
}

let passed = 0;
function ok(condition: unknown, what: string): void {
  if (!condition) throw new Error(`failed: ${what}`);
  passed++;
  console.log(`  ok: ${what}`);
}

async function json<T>(api: APIRequestContext, method: 'get' | 'post', path: string, data?: unknown): Promise<T> {
  const response = method === 'get' ? await api.get(path) : await api.post(path, { data });
  if (!response.ok())
    throw new Error(
      `${method.toUpperCase()} ${path} answered ${response.status()}: ${(await response.text()).slice(0, 300)}`,
    );
  return (await response.json()) as T;
}

const fieldsOf = (definition: Definition): Field[] =>
  definition.pages.flatMap((page) => page.groups.flatMap((group) => group.fields));

/** The texts of each answer of an entry's panel, by field key. */
const answersOf = (panel: Panel) =>
  new Map(
    panel.pages.flatMap((page) =>
      page.groups.flatMap((group) => group.answers.map((answer) => [answer.field, answer] as const)),
    ),
  );

/** A visitor sends a form through the public API as a page does: the challenge, a person's three seconds, the send. */
async function visitorSends(
  api: APIRequestContext,
  form: string,
  values: Record<string, string[]>,
): Promise<{ status: number; receipt?: { reference?: string } }> {
  // The revision a page sends is the one its markup carries.
  const markup = await (
    await api.get(`${PUBLIC}/forms/${form}/render?instance=jfb-1&page=/send/&culture=en-US`)
  ).text();
  const revision = Number(/data-jfb-revision="(\d+)"/.exec(markup)?.[1] ?? Number.NaN);
  const challenge = await json<{ challenge: string }>(api, 'post', `${PUBLIC}/forms/${form}/challenge`, {});
  await sleep(3_200);
  const response = await api.post(`${PUBLIC}/forms/${form}/entries`, {
    data: { submissionId: randomUUID(), revision, culture: 'en-US', values, challenge: challenge.challenge },
  });
  return {
    status: response.status(),
    ...((await response.json().catch(() => ({}))) as { receipt?: { reference?: string } }),
  };
}

async function entriesOf(api: APIRequestContext, form: string) {
  return json<{ items: { key: string; number: number; state: string }[]; total?: number | null }>(
    api,
    'post',
    `${MANAGEMENT}/form/${form}/entries`,
    {
      filter: { states: [], fields: [], forms: [] },
      count: true,
    },
  );
}

interface Admin {
  readonly email: string;
  readonly password: string;
}

/** A browser on a profile folder of its own, which keeps the cache and the cookies from one run to the next. */
async function browserOn(profile: string): Promise<BrowserContext> {
  // Edge locally; CI's runners have Playwright's Chromium (D19), should this ever run there.
  const context = await chromium.launchPersistentContext(profile, {
    ...(process.env.CI ? {} : { channel: 'msedge' }),
    viewport: { width: 1440, height: 900 },
  });
  // The backoffice loads several hundred modules, more than the 250 entries a browser keeps of what it fetched by
  // default, so the record is made larger before any of the page's own scripts run.
  await context.addInitScript(() => performance.setResourceTimingBufferSize(20_000));
  return context;
}

/** Signs the admin in through Umbraco's own sign-in page, when the page asks; the backoffice's header is shown after. */
async function signIn(page: Page, admin: Admin): Promise<boolean> {
  const header = page.locator('umb-backoffice-header');
  const email = page.getByLabel(/email/i).first();
  await Promise.race([header.waitFor({ timeout: 90_000 }), email.waitFor({ timeout: 90_000 })]);
  if (await header.isVisible()) return false;
  await email.fill(admin.email);
  await page
    .getByLabel(/password/i)
    .first()
    .fill(admin.password);
  await page
    .getByRole('button', { name: /^(login|log in|sign in)$/i })
    .first()
    .click();
  await header.waitFor({ timeout: 90_000 });
  return true;
}

/** The names of the backoffice chunks a page loaded, from the browser's own record of what it fetched. */
async function loadedChunks(page: Page): Promise<string[]> {
  return (await loadedPaths(page))
    .filter((path) => path.startsWith('/App_Plugins/JironFormBuilder/chunks/'))
    .map((path) => path.slice(path.lastIndexOf('/') + 1));
}

/** The paths of every file a page loaded, from the browser's own record of what it fetched. */
async function loadedPaths(page: Page): Promise<string[]> {
  return page.evaluate(() => performance.getEntriesByType('resource').map((entry) => new URL(entry.name).pathname));
}

/** The AI add-on's entry, which the site installs beside the product (18.3's review). */
const AI_ENTRY = '/App_Plugins/JironFormBuilderAI/jfb-ai.js';

/** A template that shows the form by its alias, as getting-started.md shows; Layout off, as on an empty site. */
const templateOf = (alias: string): string =>
  [
    '@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage',
    '@{ Layout = null; }',
    '<!doctype html>',
    '<html lang="en">',
    '<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>@Model.Name</title></head>',
    `<body><main><h1>@Model.Name</h1>@await Component.InvokeAsync("FormBuilderForm", new { form = "${alias}" })</main></body>`,
    '</html>',
  ].join('\n');

/** A page that shows the form, made and published through Umbraco's own Management API as a developer would; its path. */
async function pageWith(api: APIRequestContext, alias: string): Promise<string> {
  const template = randomUUID();
  const madeTemplate = await api.post(`${UMBRACO}/template`, {
    data: { id: template, name: 'Contact page', alias: 'contactPage', content: templateOf(alias) },
  });
  ok(madeTemplate.status() === 201, `a template that shows the form is created (${madeTemplate.status()})`);
  const documentType = randomUUID();
  const madeType = await api.post(`${UMBRACO}/document-type`, {
    data: {
      id: documentType,
      alias: 'contactPage',
      name: 'Contact page',
      icon: 'icon-document',
      allowedAsRoot: true,
      variesByCulture: false,
      variesBySegment: false,
      isElement: false,
      properties: [],
      containers: [],
      allowedTemplates: [{ id: template }],
      defaultTemplate: { id: template },
      cleanup: { preventCleanup: false },
      allowedDocumentTypes: [],
      compositions: [],
    },
  });
  ok(madeType.status() === 201, `a document type with that template is created (${madeType.status()})`);
  const document = randomUUID();
  const madePage = await api.post(`${UMBRACO}/document`, {
    data: {
      id: document,
      documentType: { id: documentType },
      template: { id: template },
      values: [],
      variants: [{ name: 'Contact', culture: null, segment: null }],
    },
  });
  ok(madePage.status() === 201, `a page of that type is created (${madePage.status()})`);
  const published = await api.put(`${UMBRACO}/document/${document}/publish`, {
    data: { publishSchedules: [{ culture: null }] },
  });
  ok(published.ok(), `the page is published (${published.status()})`);
  // The only page at the root answers at the site's root.
  return '/';
}

async function install(
  api: APIRequestContext,
  url: string,
  admin: Admin,
  mail: string,
  profile: string,
): Promise<void> {
  const created = await api.post(`${MANAGEMENT}/form/from-template`, {
    data: { templateKey: CONTACT_TEMPLATE, name: 'Install contact', folderKey: null },
  });
  ok(created.status() === 201, `a form is created from the contact template (${created.status()})`);
  const body = (await created.json()) as { key?: string; form?: { key: string } };
  const key = body.key ?? body.form?.key ?? '';

  // An email action to the team, saved into the draft as the Actions view saves it.
  const draft = await json<{ form: { concurrencyVersion: number }; definition: Definition & Record<string, unknown> }>(
    api,
    'get',
    `${MANAGEMENT}/form/${key}/draft`,
  );
  const action = {
    key: randomUUID(),
    type: 'email',
    name: 'Email the team',
    enabled: true,
    trigger: 'submitted',
    stopOnFailure: false,
    settings: {
      to: [TEAM],
      subject: { '': 'New contact from {{ field.name }}' },
      body: { '': '<p>{{ field.message }}</p>' },
    },
  };
  const saved = await api.put(`${MANAGEMENT}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: { ...draft.definition, actions: [action] } },
  });
  ok(saved.ok(), `an email action to the team is added (${saved.status()})`);
  const form = await json<{ concurrencyVersion: number; alias: string }>(api, 'get', `${MANAGEMENT}/form/${key}`);
  const published = await api.post(`${MANAGEMENT}/form/${key}/publish`, {
    data: { concurrencyVersion: form.concurrencyVersion },
  });
  ok(published.ok(), `it is published (${published.status()})`);
  const rendered = await api.get(`${PUBLIC}/forms/${key}/render?instance=jfb-1&page=/install/&culture=en-US`);
  ok(rendered.ok() && (await rendered.text()).includes('jfb-form'), 'the public API renders it');

  // Placed on a page: Umbraco renders the page with the form once its cache has the page.
  const path = await pageWith(api, form.alias);
  let html = '';
  for (let attempt = 0; attempt < 30 && !html.includes('jfb-form'); attempt++) {
    if (attempt > 0) await sleep(1_000);
    const page = await api.get(path);
    html = page.ok() ? await page.text() : '';
  }
  ok(html.includes('jfb-form'), `the page at ${path} renders the form`);

  const context = await browserOn(profile);
  try {
    // A visitor sends the form on the page, with the script and the stylesheets the form adds by itself.
    const visitor = await context.newPage();
    const assets: { path: string; status: number }[] = [];
    visitor.on('response', (response) => {
      const asset = new URL(response.url()).pathname;
      if (asset.startsWith('/jfb/assets/')) assets.push({ path: asset, status: response.status() });
    });
    await visitor.goto(`${url}${path}`);
    const onPage = visitor.locator('main form.jfb-form');
    await onPage.waitFor({ timeout: 30_000 });
    ok(
      assets.some((asset) => asset.path.endsWith('.css') && asset.status === 200) &&
        assets.some((asset) => asset.path.endsWith('.js') && asset.status === 200) &&
        assets.every((asset) => asset.status === 200),
      `the page loads the form's stylesheets and script (${assets.map((asset) => `${asset.path} ${asset.status}`).join(', ')})`,
    );
    const name = `Ines Install ${Date.now().toString(36)}`;
    await onPage.locator('[name="name"]').fill(name);
    await onPage.locator('[name="email"]').fill('ines@install.jfb.test');
    await onPage.locator('[name="message"]').fill('Hello from the install test.');
    // A person takes a few seconds; the form refuses a send faster than its minimum time.
    await sleep(3_500);
    await onPage.locator('button[data-jfb-nav="submit"]').click();
    await visitor.locator('main [data-jfb-outcome]').waitFor({ timeout: 30_000 });
    ok(true, 'the visitor sends it and sees its message in place of the form');

    const sent = await waitForMailIn(
      mail,
      (each) => headerOf(each.raw, 'Subject') === `New contact from ${name}`,
      60_000,
    );
    ok(headerOf(sent.raw, 'To').includes(TEAM), `the team's mail lands in the pickup folder (${sent.file})`);

    // Through the API while its sign-in holds: Umbraco ends a user's other sessions when they sign in again
    // (AllowConcurrentLogins is off by default), so the browser signs the admin in last.
    const list = await entriesOf(api, key);
    ok(list.items.length === 1 && list.items[0]?.number === 1, 'the entries list holds the entry as number 1');
    const panel = await json<Panel>(api, 'get', `${MANAGEMENT}/form/${key}/entry/${list.items[0]!.key}`);
    ok(
      [...answersOf(panel).values()].some((answer) => answer.texts.includes(name)),
      "the entry's panel shows the answers",
    );

    // The admin sees the section: Umbraco's sign-in, the header's Form Builder, the section's Overview, the entry. The
    // browser stays signed in, which the update on the floor relies on.
    const backoffice = await context.newPage();
    await backoffice.goto(`${url}/umbraco`);
    await signIn(backoffice, admin);
    await backoffice.locator('umb-backoffice-header-sections uui-tab[label="Form Builder"]').click();
    await backoffice.locator('jfb-overview').waitFor({ timeout: 60_000 });
    ok(backoffice.url().includes(SECTION), 'the admin sees the Form Builder section and opens its Overview');
    ok(
      (await loadedChunks(backoffice)).some((name) => name.startsWith('start-')),
      "the backoffice loads the package's start chunk",
    );
    ok((await loadedPaths(backoffice)).includes(AI_ENTRY), "and the AI add-on's backoffice loads beside it");
    await backoffice.goto(`${url}${SECTION}/workspace/jfb-form/edit/${key}/view/entries`);
    // A row is named by the entry's number, as the list names an entry of a form without a title field.
    const rows = backoffice.locator('jfb-entries-view tbody tr');
    await rows.first().waitFor({ timeout: 60_000 });
    const shown = (await rows.locator('th .name').allTextContents()).map((text) => text.trim());
    ok(
      shown.length === 1 && /^#1\b/.test(shown[0] ?? ''),
      `the entry shows in the form's Entries view (${shown.join('; ')})`,
    );
  } finally {
    await context.close();
  }
}

async function cache(url: string, admin: Admin, profile: string, chunk: string, oldChunk: string): Promise<void> {
  ok(chunk !== oldChunk, `the new build's start chunk (${chunk}) differs from the old one's (${oldChunk})`);
  const context = await browserOn(profile);
  try {
    // As a user comes back: the address of the section, no hard refresh, the profile's cache and cookies as they are.
    const page = await context.newPage();
    await page.goto(`${url}${SECTION}`);
    const signedInAgain = await signIn(page, admin);
    await page.locator('jfb-overview').waitFor({ timeout: 60_000 });
    ok(true, `the section opens${signedInAgain ? ' after signing in again' : ' with the kept sign-in'}`);
    const chunks = await loadedChunks(page);
    ok(
      chunks.includes(chunk),
      `the backoffice runs the new build: it loaded ${chunk} (${chunks.filter((name) => name.startsWith('start-')).join(', ')})`,
    );
    ok(!chunks.includes(oldChunk), `and not the old build's ${oldChunk}`);
  } finally {
    await context.close();
  }
}

async function upgrade(api: APIRequestContext, expected: Expected): Promise<void> {
  // The library: the folders and the forms where the baseline left them.
  const root = await json<Library>(api, 'get', `${MANAGEMENT}/tree/root?skip=0&take=100`);
  const events = expected.folders.find((folder) => folder.parentKey === null)!;
  const archive = expected.folders.find((folder) => folder.parentKey !== null)!;
  const booking = expected.forms.find((form) => form.folderKey !== null)!;
  const feedback = expected.forms.find((form) => form.folderKey === null)!;
  ok(
    root.items.some((item) => item.key === events.key && item.kind === 'folder' && item.name === events.name),
    `the folder ${events.name} is at the root`,
  );
  ok(
    root.items.some((item) => item.key === feedback.key && item.name === feedback.name && item.isPublished),
    `the published form ${feedback.name} is at the root`,
  );
  const inside = await json<Library>(api, 'get', `${MANAGEMENT}/tree/children?parentKey=${events.key}&skip=0&take=100`);
  ok(
    inside.items.some((item) => item.key === archive.key && item.name === archive.name),
    `the folder ${archive.name} is inside it`,
  );
  ok(
    inside.items.some((item) => item.key === booking.key && item.name === booking.name && item.isPublished),
    `the published form ${booking.name} is inside it`,
  );

  // The booking form's versions: two published, and a draft that changed one label since.
  const versions = await json<{ items: { version: { number: number; kind: string } }[] }>(
    api,
    'get',
    `${MANAGEMENT}/form/${booking.key}/versions?skip=0&take=50`,
  );
  ok(
    versions.items.filter((item) => item.version.kind === 'published').length === booking.publishedVersions,
    `${booking.name} keeps its ${booking.publishedVersions} published versions`,
  );
  const draft = await json<{ definition: Definition; problems: unknown[]; hasUnpublishedChanges: boolean }>(
    api,
    'get',
    `${MANAGEMENT}/form/${booking.key}/draft`,
  );
  const message = fieldsOf(draft.definition).find((field) => field.alias === 'message');
  ok(draft.problems.length === 0, `today's reader reads its draft without a problem`);
  ok(
    draft.hasUnpublishedChanges && message?.label?.[''] === booking.draftMessageLabel,
    `its draft keeps the unpublished label "${booking.draftMessageLabel}"`,
  );
  // The page's view of it: the second published version, with its phone field and the label it had, not the draft's.
  const rendered = await api.get(`${PUBLIC}/forms/${booking.key}/render?instance=jfb-1&page=/book/&culture=en-US`);
  const markup = await rendered.text();
  ok(rendered.ok() && markup.includes('jfb-form'), 'the public API renders it');
  // Each label's own words, without the marker an optional question carries after them.
  const labels = [...markup.matchAll(/<label[^>]*>([^<]*)/g)].map((match) => (match[1] ?? '').trim()).filter(Boolean);
  ok(
    markup.includes('name="phone"') && labels.includes('Phone'),
    `it renders the second published version's phone field (${labels.join(', ')})`,
  );
  ok(
    labels.includes(booking.publishedMessageLabel ?? '') && !labels.includes(booking.draftMessageLabel ?? ''),
    `it renders the published label "${booking.publishedMessageLabel}", not the draft's`,
  );

  // Every entry with its state and answers, through the entries list and each entry's panel.
  for (const form of expected.forms) {
    const list = await entriesOf(api, form.key);
    const seeded = expected.entries.filter((entry) => entry.form === form.alias);
    ok(list.items.length === seeded.length, `${form.name} lists its ${seeded.length} entries`);
    const definition = (await json<{ definition: Definition }>(api, 'get', `${MANAGEMENT}/form/${form.key}/draft`))
      .definition;
    const keyOf = new Map(fieldsOf(definition).map((field) => [field.alias ?? '', field.key]));
    for (const entry of seeded) {
      const row = list.items.find((item) => item.key === entry.key);
      ok(row?.number === entry.number && row.state === entry.state, `${form.name} #${entry.number} is ${entry.state}`);
      const answers = answersOf(await json<Panel>(api, 'get', `${MANAGEMENT}/form/${form.key}/entry/${entry.key}`));
      for (const [alias, text] of Object.entries(entry.values)) {
        const answer = answers.get(keyOf.get(alias) ?? '');
        const shown = alias === 'plan' ? answer?.files.map((file) => file.key) : answer?.texts;
        ok(shown && shown.length > 0, `${form.name} #${entry.number} shows its answer to ${alias}`);
        if (alias !== 'plan' && !['newsletter', 'room', 'arrival', 'guests', 'score'].includes(alias)) {
          ok(shown!.includes(text), `${form.name} #${entry.number}'s ${alias} is "${text}"`);
        }
      }
    }
  }

  // The file, byte for byte, through the download a user with the entries' rights gets.
  const download = await api.get(`${MANAGEMENT}/form/${booking.key}/files/${expected.file.key}`);
  const bytes = await download.body();
  ok(
    download.ok() && bytes.length === expected.file.size,
    `the file ${expected.file.name} downloads (${bytes.length} bytes)`,
  );
  ok(
    createHash('sha256').update(bytes).digest('hex') === expected.file.sha256,
    "the file's bytes are the ones the visitor sent",
  );

  // The settings the baseline saved, read through today's settings screen's API.
  const general = await json<{ values: { timeZone?: string; history?: { draftDays?: number; draftCount?: number } } }>(
    api,
    'get',
    `${MANAGEMENT}/settings/general`,
  );
  ok(general.values.timeZone === expected.settings.timeZone, `the time zone is still ${expected.settings.timeZone}`);
  ok(
    general.values.history?.draftDays === expected.settings.draftDays &&
      general.values.history.draftCount === expected.settings.draftCount,
    `the history keeps drafts ${expected.settings.draftDays} days, ${expected.settings.draftCount} at most`,
  );

  // A visitor sends the upgraded form: the entry continues the baseline's numbers, approved on arrival as a form without
  // manual approval approves it.
  const sent = await visitorSends(api, booking.key, {
    name: ['Dora After'],
    email: ['dora@upgrade.jfb.test'],
    guests: ['3'],
    room: ['double'],
  });
  ok(sent.status === 200 && sent.receipt, `a visitor sends the upgraded form (${sent.status})`);
  const after = await entriesOf(api, booking.key);
  const last = Math.max(
    ...expected.entries.filter((entry) => entry.form === booking.alias).map((entry) => entry.number),
  );
  ok(
    after.items.some((item) => item.number === last + 1 && item.state === 'approved'),
    `the new entry is number ${last + 1}, approved (the list holds ${after.items.map((item) => `#${item.number} ${item.state}`).join(', ')})`,
  );
}

async function main(): Promise<void> {
  const url = option('--url');
  const mode = option('--mode');
  const expectedFile = option('--expected');
  const mail = option('--mail');
  const profile = option('--profile');
  const chunk = option('--chunk');
  const oldChunk = option('--old-chunk');
  const usable =
    url &&
    ((mode === 'install' && mail && profile) ||
      (mode === 'upgrade' && expectedFile) ||
      (mode === 'cache' && profile && chunk && oldChunk));
  if (!usable) {
    console.error(
      [
        'usage: node install/check.ts --url <site> --mode install --mail <folder> --profile <folder>',
        '       node install/check.ts --url <site> --mode upgrade --expected <baseline-data.json>',
        '       node install/check.ts --url <site> --mode cache --profile <folder> --chunk <name> --old-chunk <name>',
      ].join('\n'),
    );
    process.exit(2);
  }
  const credentials = join(repoRoot, '.local', 'credentials.json');
  const admin = existsSync(credentials)
    ? (
        JSON.parse(readFileSync(credentials, 'utf8')) as {
          installTest?: { admin?: { email: string; password: string } };
        }
      ).installTest?.admin
    : undefined;
  if (!admin)
    throw new Error('.local/credentials.json holds no install-test admin; scripts/install-test.ps1 writes it.');
  if (mode === 'cache') {
    console.log(`install-check: cache checks against ${url}`);
    await cache(url!, admin, profile!, chunk!, oldChunk!);
    console.log(`install-check: ${passed} checks passed`);
    return;
  }
  const api = await loginThroughApi(
    { name: 'Install admin', email: admin.email, password: admin.password } as Parameters<typeof loginThroughApi>[0],
    url,
  );
  try {
    console.log(`install-check: ${mode} checks against ${url}`);
    if (mode === 'install') await install(api, url!, admin, mail!, profile!);
    else await upgrade(api, JSON.parse(readFileSync(expectedFile!, 'utf8')) as Expected);
    console.log(`install-check: ${passed} checks passed`);
  } finally {
    await api.dispose();
  }
}

main().catch((error: unknown) => {
  console.error(`install-check: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
});
