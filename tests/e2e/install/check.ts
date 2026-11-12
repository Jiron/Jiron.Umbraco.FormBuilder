// The install test's checks through the site's own APIs (ROADMAP 16.5, ADR-0240), run by scripts/install-test.ps1
// against the site it installed. The admin comes from .local/credentials.json (installTest), written by the script, and
// signs in through Umbraco's own login and PKCE flow; nothing is printed of it.
//
//   node install/check.ts --url <site> --mode install
//     a form from the built-in contact template, published, rendered by the public API, sent by a visitor and read
//     back in the entries list and the entry's panel
//   node install/check.ts --url <site> --mode upgrade --expected <baseline-data.json>
//     what the baseline stored (scripts/install-test/BaselineSeed), read back through today's APIs: the folders and
//     forms in the tree, the forms' versions and drafts, every entry's answers and state, the file's bytes, the
//     settings; then a visitor sends the upgraded form, and the entry takes the next number
//
// Exit codes: 0 every check passed, 1 a check failed (with what), 2 usage.

import type { APIRequestContext } from '@playwright/test';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { loginThroughApi } from '../support/api-login.ts';
import { repoRoot } from '../support/paths.ts';

const MANAGEMENT = '/umbraco/jfb/management/api/v1';
const PUBLIC = '/jfb/api/v1';

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

async function install(api: APIRequestContext): Promise<void> {
  const created = await api.post(`${MANAGEMENT}/form/from-template`, {
    data: { templateKey: CONTACT_TEMPLATE, name: 'Install contact', folderKey: null },
  });
  ok(created.status() === 201, `a form is created from the contact template (${created.status()})`);
  const body = (await created.json()) as { key?: string; form?: { key: string } };
  const key = body.key ?? body.form?.key ?? '';
  const form = await json<{ concurrencyVersion: number }>(api, 'get', `${MANAGEMENT}/form/${key}`);
  const published = await api.post(`${MANAGEMENT}/form/${key}/publish`, {
    data: { concurrencyVersion: form.concurrencyVersion },
  });
  ok(published.ok(), `it is published (${published.status()})`);
  const rendered = await api.get(`${PUBLIC}/forms/${key}/render?instance=jfb-1&page=/install/&culture=en-US`);
  ok(rendered.ok() && (await rendered.text()).includes('jfb-form'), 'the public API renders it');
  const sent = await visitorSends(api, key, {
    name: ['Ines Install'],
    email: ['ines@install.jfb.test'],
    message: ['Hello from the install test.'],
  });
  ok(sent.status === 200 && sent.receipt, `a visitor sends it (${sent.status})`);
  const list = await entriesOf(api, key);
  ok(list.items.length === 1 && list.items[0]?.number === 1, 'the entries list holds the entry as number 1');
  const panel = await json<Panel>(api, 'get', `${MANAGEMENT}/form/${key}/entry/${list.items[0]!.key}`);
  ok(
    [...answersOf(panel).values()].some((answer) => answer.texts.includes('Ines Install')),
    "the entry's panel shows the answers",
  );
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
  if (!url || (mode !== 'install' && mode !== 'upgrade') || (mode === 'upgrade' && !expectedFile)) {
    console.error('usage: node install/check.ts --url <site> --mode install|upgrade [--expected <baseline-data.json>]');
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
  const api = await loginThroughApi(
    { name: 'Install admin', email: admin.email, password: admin.password } as Parameters<typeof loginThroughApi>[0],
    url,
  );
  try {
    console.log(`install-check: ${mode} checks against ${url}`);
    if (mode === 'install') await install(api);
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
