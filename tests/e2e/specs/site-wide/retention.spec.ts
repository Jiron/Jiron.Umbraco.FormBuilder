import type { APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';
import { goToTab, tabsShown } from '../../support/places.ts';

// Privacy > Retention (ROADMAP 13.1, IA 9, ADR-0222): an administrator changes the days of a state and sees "What will
// be deleted" follow it before anything is saved, saves, and finds the days after a reload; a privacy officer reads the
// same screen and changes nothing. The days are put back as they were, so no other journey's site differs.

const SECTION = '/umbraco/section/jfb-forms';

interface Retention {
  readonly version: number;
  readonly days: Record<string, number>;
}

const read = async (api: APIRequestContext) => (await (await api.get(`${API}/retention`)).json()) as Retention;

/** A form whose three entries wait for approval, so they are submitted entries. */
async function seed(api: APIRequestContext, name: string): Promise<string> {
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count: 3, name, url: `${STUB_URL}/approved/${randomUUID()}` },
    timeout: 120_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  return ((await seeded.json()) as { key: string }).key;
}

test.describe.configure({ mode: 'serial' });

test('changing the days of a state, "What will be deleted" follows before the save, and the save keeps them', async ({
  page,
  browser,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const before = await read(api);
  const name = `Retention ${Date.now().toString(36)}`;
  await seed(api, name);
  try {
    await loginAs('admin');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(SECTION);
    await goToTab(page, 'Privacy', 'Retention');
    const screen = page.locator('jfb-retention-screen');
    const row = screen.locator('tbody tr', { hasText: name });
    await expect(screen.locator('.total')).toBeVisible({ timeout: 60_000 });
    await expect(row, 'a year is far off').toHaveCount(0);
    await axeInside(page, 'jfb-retention-screen');
    // A help line only under a state whose name needs one, and no raw key of the words anywhere (ROADMAP 22.4).
    await expect(screen.locator('#days-submitted-help')).toBeVisible();
    await expect(screen.locator('#days-approved-help')).toBeVisible();
    await expect(screen.locator('#days-rejected-help')).toHaveCount(0);
    await expect(screen.locator('#days-spam-help')).toHaveCount(0);
    await expect(screen.getByText(/\bjfb[A-Z]\w*_/)).toHaveCount(0);

    // Ten days for submitted entries: the form's three go within the next 30 days, counted before anything is saved.
    const submitted = screen.getByLabel('Submitted', { exact: true });
    await submitted.fill('10');
    await expect(row).toHaveCount(1);
    await expect(row.locator('td').nth(1)).toHaveText('3');
    await expect(screen.getByText('Counted with the days above, before you save.')).toBeVisible();

    // A day out of range is said under its box, and Save sends nothing.
    const spam = screen.getByLabel('Spam', { exact: true });
    await spam.fill('0');
    await spam.press('Tab');
    await expect(screen.locator('#days-spam-why')).toHaveText('Use a whole number from 1 to 3,650.');
    await expect(spam).toHaveAttribute('aria-invalid', 'true');
    await spam.fill(String(before.days['spam']));
    await spam.press('Tab');
    await expect(screen.locator('#days-spam-why')).toHaveCount(0);

    // Seven days ahead, the ten days have not come yet.
    const span = (days: number) => screen.getByRole('button', { name: `${days} days`, exact: true });
    await span(7).click();
    await expect(span(7)).toHaveAttribute('aria-pressed', 'true');
    await expect(row).toHaveCount(0);
    await span(30).click();
    await expect(row).toHaveCount(1);
    await axeInside(page, 'jfb-retention-screen');

    await screen.getByRole('button', { name: 'Save retention settings', exact: true }).click();
    await expect(
      page.locator('uui-toast-notification').filter({ hasText: 'The settings are saved.' }).last(),
    ).toBeVisible();
    await expect(screen.getByText('Counted with the days above, before you save.')).toHaveCount(0);
    expect((await read(api)).days['submitted']).toBe(10);
    await page.reload();
    await expect(page.locator('jfb-retention-screen').getByLabel('Submitted', { exact: true })).toHaveValue('10', {
      timeout: 60_000,
    });

    // A privacy officer, in a browser of their own, reads the days and the count, and changes nothing.
    const elsewhere = await browser.newContext();
    try {
      const second = await elsewhere.newPage();
      await loginAs('privacyOfficer', second);
      await second.setViewportSize({ width: 1440, height: 900 });
      await second.goto(SECTION);
      await goToTab(second, 'Privacy', 'Retention');
      const officer = second.locator('jfb-retention-screen');
      await expect(officer.locator('tbody tr', { hasText: name })).toHaveCount(1, { timeout: 60_000 });
      await expect(officer.getByText('Changing retention needs the "Manage settings" permission.')).toBeVisible();
      await expect(officer.locator('input#days-submitted')).toHaveCount(0);
      await expect(officer.getByRole('button', { name: 'Save retention settings', exact: true })).toHaveCount(0);
      expect(await tabsShown(second), 'no Data collection').toEqual(['People', 'Retention']);
      await axeInside(second, 'jfb-retention-screen');
    } finally {
      await elsewhere.close();
    }
  } finally {
    const now = await read(api);
    const back = await api.put(`${API}/settings/privacy`, {
      data: { version: now.version, values: { retention: before.days } },
    });
    expect(back.ok(), await back.text()).toBe(true);
  }
});
