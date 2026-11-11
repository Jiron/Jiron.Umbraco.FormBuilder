import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { fillForm, logicCome, press, submitForm } from '../../support/form.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { STATIC_SITE_WAIT_MS, startStaticSite, type StaticSite } from '../../support/static-site.ts';
import { walkDir, wantedScreen } from '../../support/ux-walk.ts';

// The visitor's side in each of the seven languages (ROADMAP 14.2, ADR-0230), for the review of the texts in context: a
// two-page form on the headless page, shot fresh, with the errors of an empty page and of wrong answers, on its second
// page (the progress, Back, a counter, a file box and a consent) and once sent, at a desktop's and a phone's width. It
// writes `visitor-languages/<width>/<culture>-<n>-<state>.png` beside the walk's looks; it asserts only that each state
// shows, as the review reads the words. The public API serves a site's own languages only, so it adds the five the
// demo lacks to Umbraco first; they live only in this run's fresh host, and the project runs after the walk, whose
// screens they would change.

const CULTURES = ['en-US', 'de-DE', 'fr-FR', 'it-IT', 'nl-NL', 'es-ES', 'da-DK'] as const;

/** The languages the demo lacks, as Umbraco names them. */
const ADDED: Readonly<Record<string, string>> = {
  'fr-FR': 'French (France)',
  'it-IT': 'Italian (Italy)',
  'nl-NL': 'Dutch (Netherlands)',
  'es-ES': 'Spanish (Spain)',
  'da-DK': 'Danish (Denmark)',
};

/** Adds each language the site lacks, through Umbraco's Management API. */
async function addLanguages(api: APIRequestContext): Promise<void> {
  const listed = (await (await api.get('/umbraco/management/api/v1/language?take=100')).json()) as {
    items: { isoCode: string }[];
  };
  const have = new Set(listed.items.map((language) => language.isoCode.toLowerCase()));
  for (const [isoCode, name] of Object.entries(ADDED)) {
    if (have.has(isoCode.toLowerCase())) continue;
    const added = await api.post('/umbraco/management/api/v1/language', {
      data: { name, isoCode, isDefault: false, isMandatory: false, fallbackIsoCode: null },
    });
    expect(added.ok(), await added.text()).toBe(true);
  }
}
const WIDTHS = [
  { width: 1024, height: 900 },
  { width: 375, height: 812 },
] as const;

test.skip(!wantedScreen('visitor-languages'), 'JFB_UX_ONLY names other screens');
// One worker, as the static page has one port.
test.describe.configure({ mode: 'serial' });

let site: StaticSite | undefined;
/** The form, made once per worker by its first test. */
let made: Promise<string> | undefined;

/** A headless form of two pages, its texts in the default language only, so each page shows the package's words. */
async function twoPageForm(api: APIRequestContext): Promise<string> {
  const formKey = await formWithFields(api, unique('Visitor languages'), [
    { type: 'text', alias: 'name', label: { '': 'Name' }, required: true },
    { type: 'email', alias: 'email', label: { '': 'Email' }, required: true },
    { type: 'number', alias: 'guests', label: { '': 'Guests' }, settings: { min: '1', max: '10' } },
    { type: 'phone', alias: 'phone', label: { '': 'Phone' } },
  ]);
  const draft = (await (await api.get(`${API}/form/${formKey}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: Record<string, unknown>[] } & Record<string, unknown>;
  };
  draft.definition.pages.push({
    key: crypto.randomUUID(),
    groups: [
      {
        key: crypto.randomUUID(),
        fields: [
          {
            key: crypto.randomUUID(),
            type: 'textarea',
            alias: 'message',
            label: { '': 'Message' },
            settings: { maxLength: 200, counter: true },
          },
          { key: crypto.randomUUID(), type: 'file', alias: 'attachment', label: { '': 'Attachment' } },
          {
            key: crypto.randomUUID(),
            type: 'consent',
            alias: 'agree',
            label: { '': 'Consent' },
            required: true,
            settings: { text: { '': 'I agree to the terms' } },
          },
        ],
      },
    ],
  });
  const settings = (draft.definition['settings'] ?? {}) as Record<string, Record<string, unknown>>;
  draft.definition['settings'] = { ...settings, developer: { ...settings['developer'], headless: true } };
  const saved = await api.put(`${API}/form/${formKey}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const published = await api.post(`${API}/form/${formKey}/publish`, {
    data: { concurrencyVersion: (await formOf(api, formKey)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return formKey;
}

test.beforeAll(async () => {
  // Another worker's spec may hold the static site's one port for a while (support/static-site.ts).
  test.setTimeout(STATIC_SITE_WAIT_MS + 60_000);
  site = await startStaticSite();
});

test.afterAll(async () => {
  await site?.stop();
});

async function shoot(page: Page, folder: string, culture: string, n: number, state: string): Promise<void> {
  await page.screenshot({ path: join(folder, `${culture}-${n}-${state}.png`), fullPage: true });
}

for (const size of WIDTHS) {
  for (const culture of CULTURES) {
    test(`the visitor's side, ${culture} at ${size.width}`, async ({ page, apiAs }) => {
      if (site === undefined) throw new Error('The static site did not start.');
      made ??= (async () => {
        await addLanguages(await apiAs('admin'));
        return twoPageForm(await apiAs('designer'));
      })();
      const key = await made;
      const folder = join(walkDir(), 'visitor-languages', String(size.width));
      mkdirSync(folder, { recursive: true });
      await page.setViewportSize(size);
      await page.goto(`${site.url}/?${new URLSearchParams({ form: key, culture })}`);
      const form: Locator = page.locator('jfb-form form.jfb-form');
      await expect(form).toHaveAttribute('lang', culture);
      // The first press loads the form's logic; until then it would be a post the headless page cannot take.
      await logicCome(form);
      await shoot(page, folder, culture, 1, 'fresh');

      await press(form, 'next');
      await expect(form.locator('[data-jfb-summary]')).toBeVisible();
      await shoot(page, folder, culture, 2, 'empty');

      await fillForm(form, { name: 'Ada', email: 'not-an-address', guests: '99' });
      await press(form, 'next');
      await expect(form.locator('[data-jfb-error]:not(:empty)')).not.toHaveCount(0);
      await shoot(page, folder, culture, 3, 'wrong');

      await fillForm(form, { email: `visitor-${culture}@example.test`, guests: '2' });
      await press(form, 'next');
      await expect(form.locator('textarea[name="message"]')).toBeVisible();
      await shoot(page, folder, culture, 4, 'second-page');

      await fillForm(form, { message: 'Hello', agree: true });
      const sent = await submitForm(form);
      expect(sent.state, JSON.stringify(sent)).toBe('sent');
      await shoot(page, folder, culture, 5, 'sent');
    });
  }
}
