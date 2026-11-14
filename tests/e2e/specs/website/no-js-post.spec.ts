import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import type { SubmitResult } from '../../support/form.ts';
import { demoProject } from '../../support/paths.ts';

// A visitor without JavaScript (ROADMAP 6.2h to 6.2k, ADR-0100 to ADR-0103): the demo's forms post back to their page,
// which renders again with the answers and the errors, moves through the pages with Next and Back, adds and removes
// rows, moves a ranking's options, takes files and a typed signature, or redirects to the outcome. Every page holds the newsletter form in
// its footer beside its own form. What a post stores is compared with what the runtime's post to `entries` stores for
// the same answers, through the test host's entries endpoint.

test.use({ javaScriptEnabled: false });
// The specs change the newsletter's consent and count its entries, so they run one after another.
test.describe.configure({ mode: 'serial' });

const LF = String.fromCharCode(10);
const CR = String.fromCharCode(13);
const MANAGEMENT_API = '/umbraco/jfb/management/api/v1';

interface StoredValue {
  field: string;
  row: number;
  index: number;
  text: string | null;
  textKey: string | null;
  number: number | null;
  date: string | null;
  bool: boolean | null;
}

interface StoredFile {
  field: string;
  name: string;
  size: number;
  /** The name a typed signature's PNG holds (ADR-0103), or null. */
  typed: string | null;
}

interface DefinitionField {
  key: string;
  alias?: string;
  settings?: { text?: Record<string, string> };
  fields?: DefinitionField[];
}

interface Definition {
  pages: { groups: { fields: DefinitionField[] }[] }[];
}

/** The keys of a demo form's fields by alias, children too, from its seeded definition. */
function keysOf(alias: string): Record<string, string> {
  const definition = JSON.parse(
    readFileSync(join(demoProject, 'Seeding', 'Forms', `${alias}.json`), 'utf8'),
  ) as Definition;
  const keys: Record<string, string> = {};
  const add = (fields: DefinitionField[]): void => {
    for (const field of fields) {
      if (field.alias) keys[field.alias] = field.key;
      add(field.fields ?? []);
    }
  };
  for (const page of definition.pages) for (const group of page.groups) add(group.fields);
  return keys;
}

/** What a sent form shows: the editor's message, then the entry's reference on its own line (ADR-0093). */
function outcomeOf(result: SubmitResult): { message: string; reference: string } {
  if (result.state !== 'sent') throw new Error(`The form was not sent: ${result.errors.join('; ')}`);
  const [message = '', reference = '', ...rest] = result.outcome
    .split(LF)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
  expect(rest, 'the message and the reference alone').toEqual([]);
  expect(reference).toMatch(/^Your reference is [0-9]{4}-[0-9]{5}$/);
  return { message, reference };
}

const newsletterOn = (page: Page): Locator => page.locator('[data-demo-slot="newsletter"] form.jfb-form');
const pageFormOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** The title of the page a form shows. */
const shownPage = (form: Locator): Locator => form.locator('[data-jfb-page]:not([hidden]) .jfb-title');

/**
 * Presses one of the page form's buttons by its accessible name, as a visitor without JavaScript does, and waits for
 * the page it posts back to. A move at an end is aria-disabled but still a submit button, so a click still posts it.
 */
async function press(page: Page, name: string): Promise<void> {
  const loaded = page.waitForEvent('load');
  await pageFormOn(page).getByRole('button', { name, exact: true }).click({ force: true });
  await loaded;
}

/** A ranking's options as shown: each one's position, if it has one, and its label. */
async function rankedOn(page: Page, alias: string): Promise<string[]> {
  const items = pageFormOn(page).locator(`[data-jfb-alias="${alias}"] ol.jfb-ranking > li`);
  return (await items.evaluateAll((elements) =>
    elements.map((item) =>
      [...item.children]
        .slice(0, 2)
        .map((part) => part.textContent?.trim() ?? '')
        .join(' ')
        .trim(),
    ),
  )) as string[];
}

/** Each entry of a demo form, oldest first, with its stored values. */
async function entriesOf(request: APIRequestContext, alias: string): Promise<StoredValue[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: alias } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: StoredValue[] }[] }).entries.map((entry) => entry.values);
}

/**
 * The entries of a demo form stored since a count that hold an answer only this spec gives: its own, whichever entries
 * other specs send to the same form meanwhile, since the specs run in parallel.
 */
async function ownEntries(
  request: APIRequestContext,
  alias: string,
  before: number,
  field: string | undefined,
  answer: string,
): Promise<StoredValue[][]> {
  return (await entriesOf(request, alias))
    .slice(before)
    .filter((entry) => entry.some((value) => value.field === field && value.text === answer));
}

/** Each entry of a demo form, oldest first, with its stored files. */
async function filesOf(request: APIRequestContext, alias: string): Promise<StoredFile[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: alias } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { files: StoredFile[] }[] }).entries.map((entry) => entry.files);
}

/** The files of the entries since a count that hold an answer only this spec gives, as `ownEntries` finds them. */
async function ownFiles(
  request: APIRequestContext,
  alias: string,
  before: number,
  field: string | undefined,
  answer: string,
): Promise<StoredFile[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: alias } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: StoredValue[]; files: StoredFile[] }[] }).entries
    .slice(before)
    .filter((entry) => entry.values.some((value) => value.field === field && value.text === answer))
    .map((entry) => entry.files);
}

/** A file of a length whose first bytes are a PDF's, or a program's. */
function bytesOf(head: string | number[], length: number): Buffer {
  const bytes = Buffer.alloc(length, 'x');
  (typeof head === 'string' ? Buffer.from(head, 'ascii') : Buffer.from(head)).copy(bytes);
  return bytes;
}

/** Posts the answers to the form's entries endpoint as the runtime does, with the revision and the consents' hashes the page shows. */
async function postAsTheRuntime(
  request: APIRequestContext,
  form: Locator,
  values: Record<string, unknown>,
  consents: readonly string[],
): Promise<void> {
  const key = await form.getAttribute('data-jfb-form');
  const revision = Number(await form.getAttribute('data-jfb-revision'));
  const hashes: Record<string, string> = {};
  for (const alias of consents)
    hashes[alias] = (await form.locator(`input[name="jfb-consent-${alias}"]`).getAttribute('value')) ?? '';
  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  expect(asked.status(), await asked.text()).toBe(200);
  const { challenge } = (await asked.json()) as { challenge: string };
  const response = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision, values, consents: hashes, challenge },
  });
  expect(response.status(), await response.text()).toBe(200);
}

/**
 * Saves the newsletter's consent with this wording in every language and publishes it, as an editor does while a
 * visitor has the page open; returns the wording it had.
 */
async function publishConsentWording(
  api: APIRequestContext,
  formKey: string,
  wording: Record<string, string>,
): Promise<Record<string, string>> {
  const draft = await api.get(`${MANAGEMENT_API}/form/${formKey}/draft`);
  expect(draft.status()).toBe(200);
  const { form, definition } = (await draft.json()) as { form: { concurrencyVersion: number }; definition: Definition };
  let had: Record<string, string> = {};
  for (const page of definition.pages)
    for (const group of page.groups)
      for (const field of group.fields)
        if (field.alias === 'agree') {
          had = field.settings?.text ?? {};
          field.settings = { ...field.settings, text: wording };
        }
  const saved = await api.put(`${MANAGEMENT_API}/form/${formKey}/draft`, {
    data: { concurrencyVersion: form.concurrencyVersion, definition, comment: 'The browser tests change the wording' },
  });
  expect(saved.status(), await saved.text()).toBe(200);
  const { form: savedForm } = (await saved.json()) as { form: { concurrencyVersion: number } };
  const published = await api.post(`${MANAGEMENT_API}/form/${formKey}/publish`, {
    data: { concurrencyVersion: savedForm.concurrencyVersion },
  });
  expect(published.status(), await published.text()).toBe(200);
  return had;
}

test('the newsletter beside the contact form: refused with its errors and answers kept, then sent and stored as the runtime stores it', async ({
  page,
  request,
  fillForm,
  submitForm,
}) => {
  const keys = keysOf('newsletter');
  const before = (await entriesOf(request, 'newsletter')).length;
  await page.goto('/en/contact/');
  const newsletter = newsletterOn(page);
  await expect(newsletter).toHaveAttribute('id', 'jfb-2');
  await expect(pageFormOn(page)).toHaveAttribute('id', 'jfb-1');

  await fillForm({ email: 'not an address' }, newsletter);
  const refused = await submitForm(newsletter);

  if (refused.state !== 'errors') throw new Error(`The newsletter was sent: ${refused.outcome}`);
  expect(refused.errors, 'the address, and the consent left unticked').toHaveLength(2);
  await expect(newsletterOn(page).getByLabel('Email address')).toHaveValue('not an address');
  await expect(pageFormOn(page).locator('[data-jfb-summary]')).toHaveCount(0);
  expect(new URL(page.url()).hash).toBe('#jfb-2');

  await fillForm({ email: 'ada@visitor.jfb.test', agree: true }, newsletterOn(page));
  const sent = await submitForm(newsletterOn(page));

  expect(outcomeOf(sent).message).toBe(
    'Almost done: we sent a link to ada@visitor.jfb.test. Your subscription starts once you confirm it there.',
  );
  const address = new URL(page.url());
  expect(address.pathname).toBe('/en/contact/');
  expect(address.searchParams.get('jfb-sent')).toMatch(/^jfb-2[.][A-Za-z0-9_-]+$/);
  expect(address.hash).toBe('#jfb-2');
  await expect(pageFormOn(page)).toBeVisible();

  await page.goto('/en/contact/');
  await postAsTheRuntime(request, newsletterOn(page), { email: 'ada@visitor.jfb.test', children: 'no', agree: true }, [
    'agree',
  ]);
  const entries = await ownEntries(request, 'newsletter', before, keys.email, 'ada@visitor.jfb.test');
  expect(entries).toHaveLength(2);
  expect(entries[0]).toEqual(entries[1]);
  expect(entries[0]!.filter((value) => value.field === keys.children)).toHaveLength(1);
});

test('the job application moves through its three pages with Next and Back, keeps every answer, and is stored as the runtime stores it', async ({
  page,
  request,
  fillForm,
  submitForm,
}) => {
  const keys = keysOf('application');
  const before = (await entriesOf(request, 'application')).length;
  const letter = ['I would love to work in a library.', 'I ran a reading club for six years.', '', 'Thank you.'].join(
    LF,
  );
  await page.goto('/en/careers/');
  const form = pageFormOn(page);
  await expect(shownPage(form)).toHaveText('About you');

  await form.locator('button[data-jfb-nav="next"]').click();
  await expect(pageFormOn(page).locator('[data-jfb-summary]')).toBeVisible();
  await expect(shownPage(pageFormOn(page))).toHaveText('About you');
  await expect(newsletterOn(page).locator('[data-jfb-summary]')).toHaveCount(0);

  await pageFormOn(page).locator('[name="fullName.first"]').fill('Ada');
  await pageFormOn(page).locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm({ email: 'ada@visitor.jfb.test', post: 'assistant' }, pageFormOn(page));
  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your references');
  await expect(pageFormOn(page).locator('.jfb-row')).toHaveCount(2);

  await fillForm(
    {
      'referees[0].refName': 'Grace Hopper',
      'referees[0].refContact': 'grace@visitor.jfb.test',
      'referees[1].refName': 'Mary Somerville',
      'referees[1].refContact': '+44 20 7946 0000',
    },
    pageFormOn(page),
  );
  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your letter');

  await fillForm({ letter }, pageFormOn(page));
  await pageFormOn(page).locator('button[data-jfb-nav="back"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your references');
  await expect(pageFormOn(page).locator('[name="referees[1].refName"]')).toHaveValue('Mary Somerville');

  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your letter');
  await expect(pageFormOn(page).locator('[name="letter"]')).toHaveValue(letter);
  await fillForm({ privacy: true }, pageFormOn(page));
  const consentHash = await pageFormOn(page).locator('input[name="jfb-consent-privacy"]').getAttribute('value');
  const sent = await submitForm(pageFormOn(page));

  expect(outcomeOf(sent).message).toBe('Thank you, Ada. We reply within two weeks.');
  expect(new URL(page.url()).searchParams.get('jfb-sent')).toMatch(/^jfb-1[.]/);
  await expect(newsletterOn(page)).toBeVisible();

  await page.goto('/en/careers/');
  expect(await pageFormOn(page).locator('input[name="jfb-consent-privacy"]').getAttribute('value')).toBe(consentHash);
  await postAsTheRuntime(
    request,
    pageFormOn(page),
    {
      'fullName.first': 'Ada',
      'fullName.last': 'Lovelace',
      email: 'ada@visitor.jfb.test',
      post: 'assistant',
      referees: [
        { refName: 'Grace Hopper', refContact: 'grace@visitor.jfb.test' },
        { refName: 'Mary Somerville', refContact: '+44 20 7946 0000' },
      ],
      letter,
      startEarly: 'no',
      privacy: true,
    },
    ['privacy'],
  );
  const entries = await ownEntries(request, 'application', before, keys.letter, letter);
  expect(entries).toHaveLength(2);
  expect(entries[0]).toEqual(entries[1]);
  const stored = entries[0]!;
  expect(stored.filter((value) => value.field === keys.letter).map((value) => value.text)).toEqual([letter]);
  expect(stored.some((value) => value.text?.includes(CR))).toBe(false);
  expect(stored.filter((value) => value.field === keys.refName).map((value) => [value.row, value.text])).toEqual([
    [0, 'Grace Hopper'],
    [1, 'Mary Somerville'],
  ]);
  expect(stored.filter((value) => value.field === keys.startEarly)).toHaveLength(1);
});

test('the job application adds and removes a reference without JavaScript, keeping every answer and checking nothing', async ({
  page,
  fillForm,
}) => {
  await page.goto('/en/careers/');
  await pageFormOn(page).locator('[name="fullName.first"]').fill('Ada');
  await pageFormOn(page).locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm({ email: 'ada@visitor.jfb.test', post: 'assistant' }, pageFormOn(page));
  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your references');
  const rows = pageFormOn(page).locator('.jfb-row');
  await expect(rows).toHaveCount(2);
  await expect(pageFormOn(page).getByRole('button', { name: /^Remove/ })).toHaveCount(0);
  await fillForm({ 'referees[0].refName': 'Grace Hopper', 'referees[1].refName': 'Mary Somerville' }, pageFormOn(page));

  await press(page, 'Add a reference');
  expect(new URL(page.url()).hash, 'the page opens at the row added').toBe('#jfb-1-referees-2');
  await expect(rows).toHaveCount(3);
  await expect(shownPage(pageFormOn(page))).toHaveText('Your references');
  await expect(pageFormOn(page).locator('[name="referees[0].refName"]')).toHaveValue('Grace Hopper');
  await expect(pageFormOn(page).locator('[name="referees[1].refName"]')).toHaveValue('Mary Somerville');
  await expect(pageFormOn(page).locator('[name="referees[2].refName"]')).toHaveValue('');
  await expect(pageFormOn(page).locator('[data-jfb-summary]'), 'nothing is checked: the new row is empty').toHaveCount(
    0,
  );
  await expect(pageFormOn(page).getByRole('button', { name: 'Add a reference' }), 'none past the most').toHaveCount(0);
  await expect(pageFormOn(page).locator('[name="email"]')).toHaveValue('ada@visitor.jfb.test');
  await fillForm({ 'referees[2].refName': 'Ada Byron' }, pageFormOn(page));

  await press(page, 'Remove Reference 1');
  expect(new URL(page.url()).hash, 'the page opens at the group').toBe('#jfb-1-referees');
  await expect(rows).toHaveCount(2);
  await expect(pageFormOn(page).locator('[name="referees[0].refName"]')).toHaveValue('Mary Somerville');
  await expect(pageFormOn(page).locator('[name="referees[1].refName"]')).toHaveValue('Ada Byron');
  await expect(pageFormOn(page).getByRole('button', { name: /^Remove/ }), 'none below the least').toHaveCount(0);
  await expect(pageFormOn(page).getByRole('button', { name: 'Add a reference' })).toHaveCount(1);
  await expect(pageFormOn(page).locator('[name="fullName.first"]')).toHaveValue('Ada');
  await expect(newsletterOn(page).locator('[data-jfb-summary]')).toHaveCount(0);
});

test('the feedback ranking moves options up and down and into its top three without JavaScript, keeping every answer', async ({
  page,
  request,
  fillForm,
  submitForm,
}) => {
  const keys = keysOf('feedback');
  const before = (await entriesOf(request, 'feedback')).length;
  await page.goto('/en/feedback/');
  await fillForm({ visits: 'weekly', lastVisit: '4', comments: 'More quiet, please.' }, pageFormOn(page));
  expect(await rankedOn(page, 'improve')).toEqual([
    '1 Longer opening hours',
    '2 More quiet seats',
    '3 More events for children',
    'A caf' + String.fromCharCode(0xe9),
    'Faster Wi-Fi',
  ]);

  await press(page, 'Move Longer opening hours up');
  expect(await rankedOn(page, 'improve'), 'a move past the first changes nothing').toEqual([
    '1 Longer opening hours',
    '2 More quiet seats',
    '3 More events for children',
    'A caf' + String.fromCharCode(0xe9),
    'Faster Wi-Fi',
  ]);
  await press(page, 'Move Faster Wi-Fi up');
  expect(new URL(page.url()).hash, "the page opens at the option's new place").toBe('#jfb-1-improve-place-4');
  await press(page, 'Move Faster Wi-Fi up');
  await press(page, 'Move Longer opening hours down');
  expect(await rankedOn(page, 'improve'), 'Wi-Fi in the top three, the rest in the order shown').toEqual([
    '1 More quiet seats',
    '2 Longer opening hours',
    '3 Faster Wi-Fi',
    'More events for children',
    'A caf' + String.fromCharCode(0xe9),
  ]);
  await expect(pageFormOn(page).locator('[name="comments"]')).toHaveValue('More quiet, please.');
  await expect(pageFormOn(page).locator('[name="visits"][value="weekly"]')).toBeChecked();
  await expect(pageFormOn(page).locator('[data-jfb-summary]')).toHaveCount(0);

  const sent = await submitForm(pageFormOn(page));
  expect(outcomeOf(sent).message.length).toBeGreaterThan(0);
  const entries = await ownEntries(request, 'feedback', before, keys.comments, 'More quiet, please.');
  expect(entries).toHaveLength(1);
  const improve = entries[0]!.filter((value) => value.field === keys.improve).sort((a, b) => a.index - b.index);
  expect(improve.map((value) => value.text)).toEqual(['seats', 'hours', 'wifi']);
});

test('the job application takes a CV without JavaScript: one too large and a program are refused at its field, a valid one is kept while another field fails, then stored', async ({
  page,
  request,
  fillForm,
  submitForm,
}) => {
  const keys = keysOf('application');
  const before = (await filesOf(request, 'application')).length;
  // An address only this test gives, by which its entry is told from those other specs send meanwhile.
  const email = `ada-cv-${Date.now().toString(36)}@visitor.jfb.test`;
  await page.goto('/en/careers/');
  await pageFormOn(page).locator('[name="fullName.first"]').fill('Ada');
  await pageFormOn(page).locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm({ email, post: 'assistant' }, pageFormOn(page));
  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await fillForm(
    {
      'referees[0].refName': 'Grace Hopper',
      'referees[0].refContact': 'grace@visitor.jfb.test',
      'referees[1].refName': 'Mary Somerville',
      'referees[1].refContact': '+44 20 7946 0000',
    },
    pageFormOn(page),
  );
  await pageFormOn(page).locator('button[data-jfb-nav="next"]').click();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your letter');
  await fillForm({ letter: 'I would love to work in a library.' }, pageFormOn(page));
  const cv = (): Locator => pageFormOn(page).locator('input[type="file"][name="cv"]');
  const pdf = '%PDF-1.7' + LF;

  await cv().setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: bytesOf(pdf, 2_200_000) });
  const tooLarge = await submitForm(pageFormOn(page));
  expect(tooLarge.state, 'the page, not a reset connection').toBe('errors');
  await expect(pageFormOn(page).locator('#jfb-1-cv-error')).toBeVisible();
  await expect(shownPage(pageFormOn(page))).toHaveText('Your letter');
  await expect(pageFormOn(page).locator('[name="letter"]')).toHaveValue('I would love to work in a library.');

  await cv().setInputFiles({
    name: 'cv.pdf',
    mimeType: 'application/pdf',
    buffer: bytesOf([0x4d, 0x5a, 0x90, 0x00], 600),
  });
  const program = await submitForm(pageFormOn(page));
  expect(program.state).toBe('errors');
  await expect(pageFormOn(page).locator('#jfb-1-cv-error')).toBeVisible();

  await cv().setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: bytesOf(pdf, 100_000) });
  await fillForm({ letter: '' }, pageFormOn(page));
  const noLetter = await submitForm(pageFormOn(page));
  expect(noLetter.state).toBe('errors');
  await expect(pageFormOn(page).locator('#jfb-1-cv-error')).toHaveCount(0);
  await expect(
    pageFormOn(page).getByRole('checkbox', { name: 'Keep cv.pdf (97.7 KB)' }),
    'kept while the letter is missing',
  ).toBeChecked();

  await fillForm({ letter: 'I would love to work in a library.' }, pageFormOn(page));
  expect(outcomeOf(await submitForm(pageFormOn(page))).message).toBe('Thank you, Ada. We reply within two weeks.');
  const entries = await ownFiles(request, 'application', before, keys.email, email);
  expect(entries).toEqual([[{ field: keys.cv, name: 'cv.pdf', size: 100_000, typed: null }]]);
});

test('the room hire form takes a signature typed without JavaScript: none is an error at its field, and the name typed is stored as the signature', async ({
  page,
  request,
  submitForm,
}) => {
  const keys = keysOf('roomHire');
  const before = (await filesOf(request, 'roomHire')).length;
  const day = new Date(Date.now() + 7 * 24 * 3_600_000).toISOString().slice(0, 10);
  await page.goto('/en/room-hire/');
  const form = (): Locator => pageFormOn(page);
  await form().locator('[name="day"]').fill(day);
  await form().locator('[name="from"]').fill('10:00');
  await form().locator('[name="contact.first"]').fill('Daniel');
  await form().locator('[name="contact.last"]').fill('Fernandes');
  await form().locator('[name="email"]').fill('daniel@visitor.jfb.test');
  await form().locator('[name="billing.street"]').fill('Main Street 1');
  await form().locator('[name="billing.postcode"]').fill('10115');
  await form().locator('[name="billing.city"]').fill('Berlin');
  await form().locator('[name="billing.country"]').selectOption('DE');
  await expect(form().locator('[name="signature.typed"]')).toHaveAttribute('maxlength', '100');

  const unsigned = await submitForm(form());
  expect(unsigned.state).toBe('errors');
  await expect(form().locator('#jfb-1-signature-error')).toHaveText('Add your signature, or type your name instead');
  await expect(form().locator('[name="contact.last"]')).toHaveValue('Fernandes');

  // A name with an accent, which the hand draws with its mark.
  const name = 'Daniel Fern' + String.fromCharCode(0xe1) + 'ndez';
  await form().locator('[name="signature.typed"]').fill(name);
  expect((await submitForm(form())).state).toBe('sent');
  const entries = (await filesOf(request, 'roomHire')).slice(before);
  expect(entries).toEqual([[{ field: keys.signature, name: 'signature.png', size: expect.any(Number), typed: name }]]);
});

test('a consent whose wording changed after the page was rendered comes back unticked with a notice, and is sent once ticked again', async ({
  page,
  apiAs,
  fillForm,
  submitForm,
}) => {
  await page.goto('/en/');
  const newsletter = newsletterOn(page);
  const formKey = (await newsletter.getAttribute('data-jfb-form')) ?? '';
  await fillForm({ email: 'lin@visitor.jfb.test', agree: true }, newsletter);
  const api = await apiAs('designer');
  const original = await publishConsentWording(api, formKey, {
    '': 'I agree to receive the newsletter by email. I can unsubscribe with one click.',
    de:
      'Ich m' +
      String.fromCharCode(0xf6) +
      'chte den Newsletter per E-Mail erhalten. Ich kann ihn mit einem Klick abbestellen.',
  });
  try {
    await newsletter.locator('button[data-jfb-nav="submit"]').click();

    const notice = newsletterOn(page).locator('[data-jfb-notice="form_changed"]');
    await expect(notice).toBeVisible();
    await expect(notice).toHaveAttribute('role', 'alert');
    await expect(notice.locator('h2')).toHaveText('This form was updated while you filled it in');
    await expect(notice.locator('p')).toHaveText(
      'Your answers are kept. A consent has new wording, so it is no longer ticked: read it again before you send.',
    );
    const consent = newsletterOn(page).getByRole('checkbox', {
      name: 'I can unsubscribe with one click.',
      exact: false,
    });
    await expect(consent).not.toBeChecked();
    await expect(newsletterOn(page).getByLabel('Email address')).toHaveValue('lin@visitor.jfb.test');

    await consent.check();
    expect(outcomeOf(await submitForm(newsletterOn(page))).message).toBe(
      'Almost done: we sent a link to lin@visitor.jfb.test. Your subscription starts once you confirm it there.',
    );
  } finally {
    await publishConsentWording(api, formKey, original);
  }
});
