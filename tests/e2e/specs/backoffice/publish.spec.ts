import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';

// Publishing (ROADMAP 10.3d, IA 5.7): the publish dialog says what a first version holds, and on a later publish what
// changed since the version visitors get, grouped as History groups it, with the changes not saved yet; Publish saves
// them first, so what goes live is what the dialog listed.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

/** Each field's label in the stored draft. */
async function labelsOf(api: APIRequestContext, key: string): Promise<unknown[]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
  };
  return draft.definition.pages[0]?.groups[0]?.fields.map((field) => field.label?.['']) ?? [];
}

/** Adds a field to the stored draft, as a save from elsewhere would. */
async function addField(api: APIRequestContext, key: string, label: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: unknown[] }[] }[] };
  };
  draft.definition.pages[0]?.groups[0]?.fields.push({
    key: crypto.randomUUID(),
    type: 'text',
    alias: label.toLowerCase(),
    label: { '': label },
  });
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

/** Relabels a field in the Design view's inspector, without saving. */
async function relabel(page: Page, from: string, to: string) {
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: new RegExp(`^${from}, `) }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill(to);
}

test('a first publish says what the first version holds, and a later one what changed since, unsaved too', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Publish'), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.getByRole('button', { name: /^Name, / })).toBeVisible();

  // A first publish: what the first version holds, in one sentence.
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  const dialog = page.locator('jfb-publish-modal');
  await expect(dialog.locator('.changes')).toHaveText('The first version holds 1 page and 2 fields.');
  await axeInside(page, 'jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect.poll(async () => (await formOf(api, key)).isPublished).toBe(true);

  // A field added and saved elsewhere, then a label changed here and not saved yet.
  await addField(api, key, 'Phone');
  await page.reload();
  await expect(design.getByRole('button', { name: /^Phone, / })).toBeVisible();
  await relabel(page, 'Name', 'Full name');

  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  await expect(dialog.getByRole('heading', { name: 'Since the published version' })).toBeVisible();
  await expect(dialog.locator('.changes dt')).toHaveText(['Fields']);
  await expect(dialog.locator('.changes dd li')).toHaveText(['Field added: Phone', 'Full name: Label']);
  await axeInside(page, 'jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  // What went live is what the dialog listed: the label not saved before is saved, then published.
  await expect.poll(() => labelsOf(api, key)).toEqual(['Full name', 'Email', 'Phone']);
  // The publish follows the save, so the version visitors get changes a moment after the draft.
  const liveLabels = async () => {
    const versions = (await (await api.get(`${API}/form/${key}/versions`)).json()) as {
      items: { version: { number: number; isLive: boolean } }[];
    };
    const live = versions.items.find((entry) => entry.version.isLive)?.version.number;
    const version = (await (await api.get(`${API}/form/${key}/versions/${String(live)}`)).json()) as {
      definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
    };
    return version.definition.pages[0]?.groups[0]?.fields.map((field) => field.label?.['']);
  };
  await expect.poll(liveLabels).toEqual(['Full name', 'Email', 'Phone']);

  // Published as it is, the dialog says nothing changed.
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  await expect(dialog.locator('.changes')).toHaveText('Nothing changed since the published version.');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  // Taken offline, the form is compared with the last version visitors got.
  const offline = await api.post(`${API}/form/${key}/unpublish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(offline.ok(), await offline.text()).toBe(true);
  await page.reload();
  await expect(design.getByRole('button', { name: /^Full name, / })).toBeVisible();
  await relabel(page, 'Email', 'Work email');
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  await expect(dialog.getByRole('heading', { name: 'Since the last published version' })).toBeVisible();
  await expect(dialog.locator('.changes dd li')).toHaveText(['Work email: Label']);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
});
