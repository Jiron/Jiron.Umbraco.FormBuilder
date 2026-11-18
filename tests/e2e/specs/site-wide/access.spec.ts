import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, created, unique } from '../../support/forms-api.ts';
import { ClickCounter, expectTaskClicks } from '../../support/top-tasks.ts';
import { openRowActions } from '../../support/tree.ts';
import { goToTab } from '../../support/places.ts';

// Settings > Access (ROADMAP 12.4a, IA 8, ADR-0013, ADR-0218): an administrator gives a group a permission in the
// matrix, which brings the one it needs, and saves it; then restricts a new folder to one group from its Permissions
// sidebar, which the Access list then names. The groups' grants go back as they were, so no other journey's site
// differs. Both are the administrator's top tasks 3 and 4 (docs/ux/top-tasks.md), whose clicks it counts (ROADMAP 16.6).

const SECTION = '/umbraco/section/jfb-forms';

interface Access {
  readonly groups: readonly { readonly key: string; readonly name: string; readonly permissions: readonly string[] }[];
  readonly items: readonly { readonly key: string; readonly name: string; readonly isRestricted: boolean }[];
}

const read = async (api: APIRequestContext) => (await (await api.get(`${API}/access`)).json()) as Access;

test('Settings \u203a Access gives a group a permission with the one it needs, and restricts a folder to one group', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('admin');
  const before = await read(api);
  const viewers = before.groups.find((group) => group.name === 'Entry viewers');
  const hr = before.groups.find((group) => group.name === 'HR team');
  expect(viewers && hr, 'the test host has its groups').toBeTruthy();
  const folderName = unique('Access folder');
  const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);

  await loginAs('admin');
  await page.goto(SECTION);
  const giving = new ClickCounter();
  await goToTab(page, 'Settings', 'Access', (target) => giving.click(target));
  await expect(page).toHaveURL(/workspace\/jfb-settings-access$/);
  const screen = page.locator('jfb-access-screen');
  const matrix = screen.getByRole('region', { name: 'What each user group may do with every form' });
  await expect(matrix).toBeVisible({ timeout: 60_000 });
  await axeInside(page, 'jfb-access-screen');

  try {
    await expect(matrix.getByRole('checkbox', { name: 'Design forms, Administrators' })).toBeDisabled();
    await expect(matrix.getByRole('checkbox', { name: 'Design forms, Administrators' })).toBeChecked();
    await matrix.getByRole('checkbox', { name: 'Export entries, HR team' }).check();
    await expect(matrix.getByRole('checkbox', { name: 'View entries, HR team' })).toBeChecked();
    await expect(screen.getByRole('status')).toHaveText('View entries is on too: Export entries needs it.');
    await screen.getByRole('button', { name: 'Save access', exact: true }).click();
    await expect(page.locator('uui-toast-notification').filter({ hasText: 'Access is saved.' })).toBeVisible();
    // The ticks and Save are the task's own.
    expectTaskClicks(giving, 'Administrator', 3);
    expect((await read(api)).groups.find((group) => group.key === hr!.key)?.permissions).toEqual([
      'viewEntries',
      'export',
    ]);

    // The folder's own sidebar, from its "...": only the HR team may see it.
    const closing = new ClickCounter();
    await closing.step(() => openRowActions(page, folderName));
    await closing.click(
      page.locator('umb-entity-action-list uui-menu-item[label="Permissions\u2026"]').filter({ visible: true }),
    );
    const sidebar = page.locator('jfb-permissions-modal');
    await expect(sidebar.getByText('None yet. Access given to every form reaches this folder.')).toBeVisible();
    await axeInside(page, 'jfb-permissions-modal');
    await sidebar.getByRole('combobox', { name: 'Add user group' }).selectOption({ label: 'HR team' });
    await sidebar.getByRole('checkbox', { name: 'View entries, HR team' }).check();
    await sidebar.getByRole('switch', { name: 'Only these groups' }).check();
    await sidebar.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(
      page.locator('uui-toast-notification').filter({ hasText: `The permissions of ${folderName} are saved.` }),
    ).toBeVisible();
    // The group, its permission, Only these groups and Save are the task's own.
    expectTaskClicks(closing, 'Administrator', 4);

    await page.reload();
    const item = screen.getByRole('button', { name: `Permissions for ${folderName}` });
    await expect(item).toBeVisible({ timeout: 60_000 });
    await expect(screen.locator('.item-line', { hasText: 'Only these groups: HR team' }).first()).toBeVisible();
    expect((await read(api)).items.find((one) => one.key === folder)?.isRestricted).toBe(true);
  } finally {
    // Every group but the administrators back on its grant from before, from what it holds now.
    const now = await read(api);
    const held = (key: string) => now.groups.find((group) => group.key === key)?.permissions ?? [];
    const grants = before.groups
      .filter((group) => group.name !== 'Administrators')
      .map((group) => ({ group: group.key, expected: [...held(group.key)], permissions: [...group.permissions] }))
      .filter((grant) => grant.expected.join() !== grant.permissions.join());
    const back = await api.put(`${API}/access`, { data: { grants } });
    expect(back.ok(), await back.text()).toBe(true);
    await api.post(`${API}/folder/${folder}/trash`);
  }
});
