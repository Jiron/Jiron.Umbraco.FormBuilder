import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { repoRoot } from '../../support/paths.ts';

// The privacy facts sheet holds true (ROADMAP 13.1 part 13.4, ADR-0009): a visitor fills in and sends the demo's contact
// form, and every request the page makes for its form is one the sheet lists, none goes to another host, no cookie is
// set, and every key in the browser's storage is one the sheet names. The policy templates each start with their warning.

const PRIVACY = join(repoRoot, 'docs', 'privacy');
const facts = readFileSync(join(PRIVACY, 'facts.md'), 'utf8');

/** The requests the sheet lists, as `METHOD /path` with `{name}` for a part that varies and `*` for the rest. */
const listedRequests = [...facts.matchAll(/^\| `(GET|POST) (\/jfb\/[^`]+)` \|/gm)].map(([, method, path]) => ({
  method: method ?? '',
  pattern: new RegExp(
    `^${(path ?? '')
      .replace(/[.?+^$()[\]\\|]/g, '\\$&')
      .replace(/\{[a-z]+\}/g, '[^/]+')
      .replace(/\*$/, '.*')}$`,
  ),
}));

/** The browser storage keys the sheet names, `{form}` standing for a form's key. */
const listedKeys = [...facts.matchAll(/^\| `(localStorage|sessionStorage)` \| `([^`]+)` \|/gm)].map(
  ([, area, key]) => ({
    area: area ?? '',
    pattern: new RegExp(`^${(key ?? '').replace(/\{[a-z]+\}/g, '[^:]+')}$`),
  }),
);

test('a form page makes only the requests and keeps only the storage the privacy facts sheet lists', async ({
  page,
}) => {
  test.setTimeout(120_000);
  expect(listedRequests.length, 'the sheet lists the requests').toBeGreaterThan(5);
  const contact = formPages().find((each) => each.form === 'contact');
  if (contact === undefined) throw new Error('The demo has no contact page.');
  const made: { method: string; url: URL }[] = [];
  page.on('request', (request) => made.push({ method: request.method(), url: new URL(request.url()) }));

  await page.goto(pathOf(contact.texts.en, 'en'));
  const form = page.locator('main form.jfb-form');
  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
  await form.locator('[name="message"]').fill('Is the reading room open on Sundays?');
  await form.locator('button[data-jfb-nav="submit"]').click();
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible({ timeout: 30_000 });
  // The insights' beacons leave as the page goes.
  await page.goto('about:blank');

  const origin = made[0]?.url.origin;
  const elsewhere = made.filter((request) => request.url.protocol.startsWith('http') && request.url.origin !== origin);
  const ours = made.filter((request) => request.url.origin === origin && request.url.pathname.startsWith('/jfb/'));
  const unlisted = ours.filter(
    (request) =>
      !listedRequests.some((listed) => listed.method === request.method && listed.pattern.test(request.url.pathname)),
  );
  expect(
    elsewhere.map((request) => request.url.href),
    'no other host',
  ).toEqual([]);
  expect(ours.length, 'the form made its requests').toBeGreaterThan(2);
  expect(
    ours.some((request) => request.url.pathname.endsWith('/entries')),
    'and sent',
  ).toBe(true);
  expect(
    unlisted.map((request) => `${request.method} ${request.url.pathname}`),
    'each is in the sheet',
  ).toEqual([]);
  expect(await page.context().cookies(), 'no cookie').toEqual([]);
});

test('the storage a form page keeps is what the sheet names: nothing without "Save and continue later"', async ({
  page,
}) => {
  const contact = formPages().find((each) => each.form === 'contact');
  if (contact === undefined) throw new Error('The demo has no contact page.');
  await page.goto(pathOf(contact.texts.en, 'en'));
  const form = page.locator('main form.jfb-form');
  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="message"]').fill('Hello');
  const kept = await page.evaluate(async () => ({
    localStorage: Object.keys(window.localStorage),
    sessionStorage: Object.keys(window.sessionStorage),
    databases: ((await indexedDB.databases?.()) ?? []).map((database) => database.name ?? ''),
  }));
  const unnamed = [
    ...kept.localStorage.map((key) => ['localStorage', key] as const),
    ...kept.sessionStorage.map((key) => ['sessionStorage', key] as const),
  ].filter(([area, key]) => !listedKeys.some((listed) => listed.area === area && listed.pattern.test(key)));
  expect(unnamed, 'each key is in the sheet').toEqual([]);
  expect(kept.databases, 'no database').toEqual([]);
  expect(
    listedKeys.map((listed) => listed.area),
    'the sheet names the one key of Save and continue later',
  ).toEqual(['sessionStorage']);
  expect(await page.context().cookies(), 'no cookie').toEqual([]);
});

test('each privacy-policy template starts with its warning', () => {
  for (const language of ['en', 'de']) {
    const template = readFileSync(join(PRIVACY, `policy-template.${language}.md`), 'utf8');
    expect(template.split('\n')[0], language).toBe(
      'TEMPLATE, not legal advice; adapt with your data protection adviser.',
    );
  }
});
