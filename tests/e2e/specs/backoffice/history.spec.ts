import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';

// A form's History (ROADMAP 10.3a, IA 5.6, ADR-0196): a form published twice, the second time with a field renamed and
// required and a field added, then saved once more. The list shows the publishes newest first with what changed and
// the live one's chip; Show draft saves adds the saves and the current draft; two ticked versions compare in words;
// a version's name compares it with the current draft; a pin and a comment from a row's "..." stay after a reload.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: { pages: { groups: { fields: Record<string, unknown>[] }[] }[] };
}

/** Changes the draft's fields and saves it. */
async function saveFields(api: APIRequestContext, key: string, change: (fields: Record<string, unknown>[]) => void) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;
  const fields = draft.definition.pages[0]?.groups[0]?.fields ?? [];
  change(fields);
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

async function publish(api: APIRequestContext, key: string) {
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

test("a form's History lists its versions, compares two in words, and keeps a pin and a comment", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('History'), [
    { type: 'email', alias: 'email', label: { '': 'Your email' } },
  ]);
  await publish(api, key);
  await saveFields(api, key, (fields) => {
    const email = fields[0] as Record<string, unknown>;
    email['label'] = { '': 'Email address' };
    email['required'] = true;
    fields.push({ key: randomUUID(), type: 'text', alias: 'order', label: { '': 'Order number' } });
  });
  await publish(api, key);
  await saveFields(api, key, (fields) => {
    (fields[1] as Record<string, unknown>)['label'] = { '': 'Order reference' };
  });
  const listed = (await (await api.get(`${API}/form/${key}/versions`)).json()) as {
    items: { version: { number: number } }[];
  };
  const [second, first] = listed.items.map((item) => item.version.number);
  expect([first, second].every((number) => typeof number === 'number')).toBe(true);

  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/history`);
  const view = page.locator('jfb-form-history-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(2);
  const row = (number: number | undefined) => view.locator(`tbody tr[data-number="${String(number)}"]`);
  await expect(row(second).locator('.title')).toHaveText(
    new RegExp(`^\\s*Version ${String(second)}\\s*Published\\s*$`),
  );
  await expect(row(second).locator('.changed')).toHaveText(/^\s*2 fields\s*$/);
  await expect(row(first).locator('.changed')).toHaveText(/^\s*First published version\s*$/);
  await expect(row(second).locator('.made')).toHaveText(/^Today, .+ \u00b7 .+$/);
  await axeInside(page, 'jfb-form-history-view');

  // The draft saves, muted, with the current draft first.
  await view.locator('uui-toggle').click();
  await expect(rows.first().locator('.plain')).toHaveText('Current draft');
  await expect(view.locator('tbody tr.draft').first()).toBeVisible();
  await expect(view.locator('.note')).toHaveText('Published and pinned versions are kept for good.');
  await view.locator('uui-toggle').click();
  await expect(rows).toHaveCount(2);

  // Two versions compared, the older first, in words.
  // Umbraco's box draws itself over its input, so the box is clicked and its input, named by the version, is checked.
  for (const number of [first, second]) {
    await row(number).locator('uui-checkbox').click();
    await expect(view.getByRole('checkbox', { name: `Select Version ${String(number)}`, exact: true })).toBeChecked();
  }
  await view.getByRole('button', { name: 'Compare', exact: true }).click();
  const comparison = page.locator('jfb-version-comparison-modal');
  await expect(comparison.locator('umb-body-layout')).toHaveAttribute(
    'headline',
    `Version ${String(first)} and version ${String(second)}`,
  );
  const fieldsPart = comparison.locator('.part[data-part="fields"]');
  await expect(fieldsPart.locator('li')).toHaveText(['Field added: Order number']);
  await expect(fieldsPart.locator('tbody tr').filter({ hasText: 'Email address, Label' }).locator('td')).toHaveText([
    'Your email',
    'Email address',
  ]);
  await expect(fieldsPart.locator('tbody tr').filter({ hasText: 'Email address, Required' }).locator('td')).toHaveText([
    'No',
    'Yes',
  ]);
  await axeInside(page, 'jfb-version-comparison-modal');
  await comparison.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(comparison).toHaveCount(0);

  // A version compared with the current draft.
  await view
    .getByRole('button', { name: `Version ${String(second)}, compared with the current draft`, exact: true })
    .click();
  await expect(comparison.locator('umb-body-layout')).toHaveAttribute(
    'headline',
    `Version ${String(second)} and the current draft`,
  );
  await expect(comparison.locator('tbody tr').filter({ hasText: 'Order reference, Label' }).locator('td')).toHaveText([
    'Order number',
    'Order reference',
  ]);
  await comparison.getByRole('button', { name: 'Close', exact: true }).click();

  // A pin and a comment, which stay.
  await row(first)
    .getByRole('button', { name: `More actions for Version ${String(first)}` })
    .click();
  await row(first).getByRole('button', { name: 'Pin', exact: true }).click();
  await expect(row(first).locator('uui-icon.pin')).toBeVisible();
  await row(second)
    .getByRole('button', { name: `More actions for Version ${String(second)}` })
    .click();
  await row(second).getByRole('button', { name: 'Add comment\u2026', exact: true }).click();
  const comment = page.locator('jfb-version-comment-modal');
  await comment.locator('uui-textarea textarea').fill('Order numbers for the autumn');
  await axeInside(page, 'jfb-version-comment-modal');
  await comment.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(row(second).locator('.comment-column')).toHaveText('Order numbers for the autumn');
  await page.reload();
  await expect(row(first).locator('uui-icon.pin')).toBeVisible();
  await expect(row(second).locator('.comment-column')).toHaveText('Order numbers for the autumn');

  // Published from the History, a new version is live, which the list shows without a reload.
  await page.getByRole('button', { name: 'Publish' + String.fromCharCode(0x2026) }).click();
  await page.locator('jfb-publish-modal').getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(rows).toHaveCount(3);
  const now = (await (await api.get(`${API}/form/${key}/versions`)).json()) as typeof listed;
  const live = now.items[0]?.version.number;
  expect(live).toBeGreaterThan(second ?? 0);
  await expect(row(live).locator('.title')).toHaveText(new RegExp(`^\\s*Version ${String(live)}\\s*Published\\s*$`));
  await expect(row(second).locator('uui-tag')).toHaveCount(0);
  await expect(row(live).locator('.changed')).toHaveText(/^\s*Order reference\s*$/);
});
