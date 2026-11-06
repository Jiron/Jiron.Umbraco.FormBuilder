import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';

// Restoring a version as a new draft (ROADMAP 10.3b, IA 5.6, ADR-0198): a form published with "Your email", then saved
// with "Email address". From the sidebar that compares the published version with the current draft, a restore names
// what changed and Undo brings the draft before back; a row's "..." restores at once; Restore an earlier version... in
// the form's "..." saves a change not saved yet first, so Undo brings it back; and Restore and publish... opens the
// publish dialog for the restored draft. The sidebar's buttons sit at its foot, stacked on a phone.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: { pages: { groups: { fields: Record<string, unknown>[] }[] }[] };
}

/** The label of the form's only field, as its draft holds it. */
async function labelOf(api: APIRequestContext, key: string): Promise<unknown> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;
  return (draft.definition.pages[0]?.groups[0]?.fields[0]?.['label'] as Record<string, string> | undefined)?.[''];
}

/** A form published with "Your email", then saved with "Email address"; the published version's number. */
async function publishedThenChanged(api: APIRequestContext, name: string): Promise<{ key: string; published: number }> {
  const key = await formWithFields(api, name, [{ type: 'email', alias: 'email', label: { '': 'Your email' } }]);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;
  (draft.definition.pages[0]?.groups[0]?.fields[0] as Record<string, unknown>)['label'] = { '': 'Email address' };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const versions = (await (await api.get(`${API}/form/${key}/versions`)).json()) as {
    items: { version: { number: number } }[];
  };
  return { key, published: versions.items[0]?.version.number ?? 0 };
}

const toast = (page: Page) => page.locator('jfb-undo-notification').last();

test('a version comes back as a new draft from its sidebar, and Undo brings the draft before back', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, published } = await publishedThenChanged(api, unique('Restore sidebar'));
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/history`);
  const view = page.locator('jfb-form-history-view');

  await view
    .getByRole('button', { name: `Version ${String(published)}, compared with the current draft`, exact: true })
    .click();
  const sidebar = page.locator('jfb-version-comparison-modal');
  await expect(sidebar.locator('.part[data-part="fields"]')).toBeVisible();
  await axeInside(page, 'jfb-version-comparison-modal');
  await sidebar.getByRole('button', { name: 'Restore as a new draft', exact: true }).click();
  await expect(sidebar).toHaveCount(0);

  await expect(toast(page)).toContainText(
    new RegExp(`^\\s*Version ${String(published)} is the new draft: .+ changed\\.`),
  );
  await expect.poll(() => labelOf(api, key)).toBe('Your email');
  await toast(page).getByRole('button', { name: 'Undo', exact: true }).click();
  // In the toast, not only in Umbraco's announcer, which says it too.
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'The draft before is back.' })).toBeVisible();
  await expect.poll(() => labelOf(api, key)).toBe('Email address');
});

test("the sidebar's buttons sit at its foot, and a phone stacks them, the primary first and whole", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, published } = await publishedThenChanged(api, unique('Restore footer'));
  await loginAs('designer');
  for (const [width, height] of [
    [1440, 900],
    [375, 812],
  ] as const) {
    await page.setViewportSize({ width, height });
    await page.goto(`${SECTION}/${key}/view/history`);
    await page
      .locator('jfb-form-history-view')
      .getByRole('button', { name: `Version ${String(published)}, compared with the current draft`, exact: true })
      .click();
    const sidebar = page.locator('jfb-version-comparison-modal');
    const buttons = sidebar.locator('.actions uui-button');
    await expect(buttons).toHaveCount(2);
    const boxes = async () => Promise.all([0, 1].map(async (at) => buttons.nth(at).boundingBox()));
    // The sidebar slides in: the buttons are measured once they rest.
    let before = '';
    await expect
      .poll(
        async () => {
          const now = JSON.stringify(await boxes());
          const resting = now === before;
          before = now;
          return resting;
        },
        { intervals: [250] },
      )
      .toBe(true);
    const [close, restore] = await boxes();
    if (!close || !restore) throw new Error('The buttons have no boxes');
    // At the sidebar's foot, as Umbraco's own sidebars have them.
    expect(height - Math.max(close.y + close.height, restore.y + restore.height)).toBeLessThan(24);
    // Every button whole in the window.
    for (const box of [close, restore]) expect(box.x + box.width).toBeLessThanOrEqual(width);
    if (width === 375) {
      // Stacked: the primary above Close, both the sidebar's width.
      expect(restore.y).toBeLessThan(close.y);
      expect(Math.abs(restore.width - close.width)).toBeLessThan(2);
    } else expect(restore.x).toBeGreaterThan(close.x);
    await sidebar.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(sidebar).toHaveCount(0);
  }
});

test('a row\'s "..." restores a version at once', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, published } = await publishedThenChanged(api, unique('Restore row'));
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/history`);
  const row = page.locator(`jfb-form-history-view tbody tr[data-number="${String(published)}"]`);

  await row.getByRole('button', { name: `More actions for Version ${String(published)}` }).click();
  await row.getByRole('button', { name: 'Restore as a new draft', exact: true }).click();
  await expect(toast(page)).toContainText(`Version ${String(published)} is the new draft`);
  await expect.poll(() => labelOf(api, key)).toBe('Your email');
  // The list reads again: the new draft is a version of its own, the one before stays.
  await page.locator('jfb-form-history-view uui-toggle').click();
  await expect(page.locator('jfb-form-history-view tbody tr').first().locator('.plain')).toHaveText('Current draft');
});

test('Restore an earlier version... in the form\'s "..." saves a change first, which Undo brings back', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, published } = await publishedThenChanged(api, unique('Restore earlier'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: /^Email address, / }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill('Work email');

  await page.locator('umb-workspace-entity-action-menu').getByRole('button', { name: 'Actions' }).click();
  const menu = page.locator('umb-entity-action-list uui-menu-item').filter({ visible: true });
  await expect(menu.first()).toHaveAttribute('label', 'Restore an earlier version' + String.fromCharCode(0x2026));
  await menu.first().click();

  const picker = page.locator('jfb-version-picker-modal');
  await expect(picker.locator('.version')).toHaveCount(1);
  await axeInside(page, 'jfb-version-picker-modal');
  await picker.locator(`.version[data-number="${String(published)}"]`).click();
  const sidebar = page.locator('jfb-version-comparison-modal');
  await sidebar.getByRole('button', { name: 'Restore as a new draft', exact: true }).click();
  await expect(picker).toHaveCount(0);
  await expect(sidebar).toHaveCount(0);

  await expect.poll(() => labelOf(api, key)).toBe('Your email');
  await expect(design.getByRole('button', { name: /^Your email, / })).toBeVisible();
  await toast(page).getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(() => labelOf(api, key)).toBe('Work email');
  await expect(design.getByRole('button', { name: /^Work email, / })).toBeVisible();
});

test('Restore and publish... restores, then publishes the restored draft from the publish dialog', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, published } = await publishedThenChanged(api, unique('Restore publish'));
  // "Email address" goes live, so the first publish, "Your email", is an earlier version to bring back and publish.
  const again = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(again.ok(), await again.text()).toBe(true);
  const second =
    ((await (await api.get(`${API}/form/${key}/versions`)).json()) as { items: { version: { number: number } }[] })
      .items[0]?.version.number ?? 0;
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/history`);

  await page
    .locator('jfb-form-history-view')
    .getByRole('button', { name: `Version ${String(published)}, compared with the current draft`, exact: true })
    .click();
  await page
    .locator('jfb-version-comparison-modal')
    .getByRole('button', { name: 'Restore and publish' + String.fromCharCode(0x2026), exact: true })
    .click();
  const dialog = page.locator('jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  // The dialog closes as it is confirmed; the publish follows, and the list then holds a newer live version.
  const live = async () => {
    const versions = (await (await api.get(`${API}/form/${key}/versions`)).json()) as {
      items: { version: { number: number; isLive: boolean } }[];
    };
    const newest = versions.items[0]?.version;
    return newest?.isLive === true ? newest.number : 0;
  };
  await expect.poll(live).toBeGreaterThan(second);
  await expect(page.locator('jfb-form-history-view tbody tr').first().locator('uui-tag')).toHaveText('Published');
  expect(await labelOf(api, key)).toBe('Your email');
});
