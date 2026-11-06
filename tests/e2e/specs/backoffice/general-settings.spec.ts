import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';

// Settings > General (ROADMAP 10.3b, IA 8, ADR-0198): how long a form's History keeps draft saves. An administrator
// opens it from the sidebar, finds a value out of range said under its box before anything is sent, saves two new
// values, and finds them after a reload; the values go back as they were, so no other journey's site keeps fewer.

const SCREEN = '/umbraco/section/jfb-forms/workspace/jfb-settings-general';

interface General {
  readonly version: number;
  readonly history: { readonly draftDays: number; readonly draftCount: number };
}

const read = async (api: APIRequestContext) => (await (await api.get(`${API}/settings/general`)).json()) as General;

test('Settings \u203a General keeps draft saves as long as it is told, and says a value out of range first', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api);
  await loginAs('admin');
  await page.goto('/umbraco/section/jfb-forms');
  await page.locator('umb-menu-item-layout[label="General"], uui-menu-item[label="General"]').first().click();
  await expect(page).toHaveURL(new RegExp(`${SCREEN}$`));

  const screen = page.locator('jfb-general-settings');
  const days = screen.getByRole('spinbutton', { name: 'Keep draft saves for' });
  const count = screen.getByRole('spinbutton', { name: 'Always keep the newest' });
  await expect(days).toHaveValue(String(before.history.draftDays));
  await expect(count).toHaveValue(String(before.history.draftCount));
  await axeInside(page, 'jfb-general-settings');

  try {
    await days.fill('0');
    await screen.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(screen.locator('#draftDays-why')).toHaveText('Use a whole number from 1 to 3650.');
    await expect(days).toBeFocused();
    expect((await read(api)).version).toBe(before.version);

    await days.fill('30');
    await count.fill('5');
    await screen.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.locator('uui-toast-notification').filter({ hasText: 'The settings are saved.' })).toBeVisible();
    await expect(screen.locator('#draftDays-why')).toHaveCount(0);
    await page.reload();
    await expect(days).toHaveValue('30');
    await expect(count).toHaveValue('5');
    expect((await read(api)).history).toEqual({ draftDays: 30, draftCount: 5 });
  } finally {
    const now = await read(api);
    const back = await api.put(`${API}/settings/general`, { data: { version: now.version, history: before.history } });
    expect(back.ok(), await back.text()).toBe(true);
  }
});
