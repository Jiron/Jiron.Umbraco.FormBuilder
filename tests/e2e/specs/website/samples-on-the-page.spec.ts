import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axe } from '../../support/axe.ts';
import { formOn, submitForm } from '../../support/form.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { pageWith } from '../../support/form-page.ts';
import { STATIC_SITE_WAIT_MS, startStaticSite } from '../../support/static-site.ts';

// The colour sample on the website (ROADMAP 15.2, ADR-0234), a site's own field type: on a Razor page its view writes a
// radio button for each colour with its swatch and its name, under the field's legend, which the runtime checks as any
// group of choices and posts as the colour's value; through <jfb-form> on a headless page the control the sample's
// script registers writes the same, and the entry holds the colour chosen. Axe is clean on both.

test.describe.configure({ mode: 'serial' });

/** A published headless form with a name and a required colour field with the default colours; its key and alias. */
async function colourForm(api: APIRequestContext) {
  const key = await formWithFields(api, unique('Colours'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' } },
    { type: 'sample.colour', alias: 'favourite', label: { '': 'Favourite colour' }, required: true },
  ]);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: { developer?: Record<string, unknown> } };
  };
  draft.definition.settings.developer = { ...draft.definition.settings.developer, headless: true };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const form = (await (await api.get(`${API}/form/${key}`)).json()) as { alias: string; concurrencyVersion: number };
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: form.concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return { key, alias: form.alias };
}

/** The colour stored for each of the form's entries, oldest first: its one value that is a colour's. */
async function coloursStored(page: Page, alias: string): Promise<string[]> {
  const response = await page.request.get(`/jfb-testing/entries?form=${encodeURIComponent(alias)}`);
  expect(response.ok(), await response.text()).toBe(true);
  const { entries } = (await response.json()) as { entries: { values: { text: string | null }[] }[] };
  return entries.map((entry) =>
    entry.values
      .map((value) => value.text ?? '')
      .filter((text) => text.startsWith('#'))
      .join(','),
  );
}

/** The field's group, named by its legend, and its three colours, named by their names. */
async function expectTheColours(form: Locator): Promise<Locator> {
  const group = form.getByRole('group', { name: /Favourite colour/ });
  await expect(group).toBeVisible();
  await expect(group.getByRole('radio')).toHaveCount(3);
  for (const name of ['Blue', 'Green', 'Red']) await expect(group.getByRole('radio', { name })).toBeVisible();
  await expect(group.locator('svg circle').first()).toHaveAttribute('fill', '#1f6feb');
  return group;
}

test('the colour field on a Razor page: a radio button for each colour under its legend, checked and posted as its value', async ({
  page,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('designer');
  const { key, alias } = await colourForm(api);
  const editor = await apiAs('editor');
  const path = await pageWith(editor, key, 'Colour page');

  await expect.poll(async () => (await page.goto(path))?.status(), { timeout: 30_000 }).toBe(200);
  const form = formOn(page, key);
  const group = await expectTheColours(form);
  await axe(page, 'the colour field on a Razor page', true);

  // Required: sent without a colour, the visitor is told at the field, and every colour is marked.
  await form.getByLabel('Your name').fill('Ada');
  const refused = await submitForm(form);
  expect(refused.state).toBe('errors');
  await expect(group.getByRole('radio', { name: 'Blue' })).toHaveAttribute('aria-invalid', 'true');

  await group.getByRole('radio', { name: 'Green' }).check();
  expect((await submitForm(form)).state).toBe('sent');
  await expect.poll(() => coloursStored(page, alias)).toEqual(['#2da44e']);
});

test('the colour field through <jfb-form> on a headless page: the control the sample registers, sent as its value', async ({
  page,
  apiAs,
}) => {
  test.setTimeout(STATIC_SITE_WAIT_MS + 120_000);
  const api = await apiAs('designer');
  const { key, alias } = await colourForm(api);
  const site = await startStaticSite();
  try {
    const query = new URLSearchParams({ form: key, culture: 'en-US', script: '/jfb-samples/colour-control.js' });
    await page.goto(`${site.url}/?${query}`);
    const form = formOn(page, key);
    const group = await expectTheColours(form);
    await axe(page, 'the colour field through the element', true);

    await group.getByRole('radio', { name: 'Red' }).check();
    expect((await submitForm(form)).state).toBe('sent');
    await expect.poll(() => coloursStored(page, alias)).toEqual(['#cf222e']);
  } finally {
    await site.stop();
  }
});
