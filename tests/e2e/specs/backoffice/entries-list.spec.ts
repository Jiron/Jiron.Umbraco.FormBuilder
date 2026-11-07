import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';

// A form's entries list (ROADMAP 11.1, IA 6.1, ADR-0207), with 1,000 entries sent through the submission pipeline that
// wait for approval, and a webhook on approval. The list counts them and pages 50 at a time; a search finds one by
// its name and by its number; a filter set saved here opens in a second browser; a bulk approval is undone from its
// toast inside the window, every entry back in its state and no approval's webhook sent, and once the window has
// passed the toast offers no Undo (ADR-0023).

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Page {
  readonly items: { key: string; state: string }[];
  readonly total?: number;
}

/** The entries of the form in a state, as the list's API answers them. */
async function inState(api: APIRequestContext, form: string, state: string): Promise<Page> {
  const answer = await api.post(`${API}/form/${form}/entries`, {
    data: { filter: { states: [state], fields: [] }, columns: [], count: true },
  });
  expect(answer.ok(), await answer.text()).toBe(true);
  return (await answer.json()) as Page;
}

/** The calls the stub had on the approval webhook's path. */
async function approvalCalls(api: APIRequestContext, path: string): Promise<number> {
  const calls = (await (await api.get(`${STUB_URL}/_stub/calls`)).json()) as { path: string }[];
  return calls.filter((call) => call.path === path).length;
}

test('the entries list pages 1,000 entries, searches, keeps a filter set across browsers and undoes a bulk approval', async ({
  page,
  browser,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(900_000);
  const api = await apiAs('admin');
  const hook = `/approved/${String(Date.now())}`;
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count: 1000, name: `Applications ${String(Date.now())}`, url: `${STUB_URL}${hook}` },
    timeout: 600_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  const { key } = (await seeded.json()) as { key: string };

  await page.clock.install();
  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/entries`);
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(50, { timeout: 60_000 });
  await expect(view.locator('.count')).toHaveText('1,000 entries');
  await expect(rows.first().locator('th .name')).toHaveText('Person 1000, unseen');
  await axeInside(page, 'jfb-entries-view');

  // Show more adds the next 50.
  await view.getByRole('button', { name: 'Show more', exact: true }).click();
  await expect(rows).toHaveCount(100);
  await expect(rows.nth(50).locator('th .name')).toContainText('Person 0950');

  // A search by a name, and by a number.
  const search = view.locator('uui-input.search input');
  await search.fill('Person 0999');
  await search.press('Enter');
  await expect(rows).toHaveCount(1);
  await expect(view.locator('.count')).toHaveText('1 entry');
  await search.fill('#42');
  await search.press('Enter');
  await expect(rows).toHaveCount(1);
  await expect(rows.first().locator('th .name')).toContainText('Person 0042');
  await search.fill('');
  await search.press('Enter');
  await expect(rows).toHaveCount(50);

  // A filter set: the accountants waiting for approval, saved under a name.
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'Position', exact: true }).click();
  const filter = page.locator('jfb-entry-filter-modal');
  await filter.locator('uui-checkbox[label="Accountant"]').click();
  await filter.getByRole('button', { name: 'Apply filter', exact: true }).click();
  await expect(view.locator('.chip-text')).toHaveText(['Position: Accountant']);
  await expect(view.locator('.count')).toHaveText('250 entries');
  await view.getByRole('button', { name: 'Save as\u2026', exact: true }).click();
  const naming = page.locator('jfb-entry-set-name-modal');
  await naming.locator('uui-input input').fill('Accountants');
  await naming.getByRole('button', { name: 'Save filter set', exact: true }).click();
  await expect(naming).toHaveCount(0);

  // The same user in a second browser finds the set, and it filters there too.
  const elsewhere = await browser.newContext();
  try {
    const second = await elsewhere.newPage();
    await loginAs('entryHandler', second);
    await second.setViewportSize({ width: 1440, height: 900 });
    await second.goto(`${SECTION}/${key}/view/entries`);
    const there = second.locator('jfb-entries-view');
    await expect(there.locator('tbody tr')).toHaveCount(50, { timeout: 60_000 });
    await there.getByRole('button', { name: 'Filter', exact: true }).click();
    await there.locator('#filter-menu').getByRole('button', { name: 'Accountants', exact: true }).click();
    await expect(there.locator('.chip-text')).toHaveText(['Position: Accountant']);
    await expect(there.locator('.count')).toHaveText('250 entries');
  } finally {
    await elsewhere.close();
  }

  // A bulk approval undone from its toast inside the window.
  await view.getByRole('button', { name: 'Clear filters', exact: true }).click();
  await expect(view.locator('.count')).toHaveText('1,000 entries');
  const ticked = await rows.evaluateAll((all) => all.slice(0, 3).map((row) => (row as HTMLElement).dataset['key']));
  for (const index of [0, 1, 2]) await rows.nth(index).locator('uui-checkbox').click();
  await expect(view.locator('.bulk .selected')).toHaveText('3 selected');
  await view.locator('.bulk').getByRole('button', { name: 'Approve', exact: true }).click();
  const toast = page.locator('jfb-undo-notification').last();
  await expect(toast).toContainText('3 entries approved.');
  await expect(rows.first().locator('uui-tag')).toHaveText('Approved');
  await toast.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(rows.first().locator('uui-tag')).toHaveText('Waiting for approval');
  await expect.poll(async () => (await inState(api, key, 'approved')).total).toBe(0);
  const waiting = (await inState(api, key, 'submitted')).items.map((item) => item.key);
  expect(ticked.every((one) => one !== undefined && waiting.includes(one))).toBe(true);

  // Past the window on the server, the undone change sends no approval's webhook.
  const moved = await request.post('/jfb-testing/clock', { data: { advanceSeconds: 30 } });
  try {
    expect(moved.ok(), await moved.text()).toBe(true);
    await page.waitForTimeout(5_000);
    expect(await approvalCalls(api, hook)).toBe(0);
  } finally {
    await request.delete('/jfb-testing/clock');
  }

  // Another approval: once its window has passed, its toast offers no Undo and says it is too late.
  await rows.nth(3).locator('uui-checkbox').click();
  await view.locator('.bulk').getByRole('button', { name: 'Approve', exact: true }).click();
  await expect(toast.getByRole('button', { name: 'Undo', exact: true })).toBeVisible();
  await page.clock.fastForward(11_000);
  await expect(toast.getByRole('button', { name: 'Undo', exact: true })).toHaveCount(0);
  await expect(toast).toContainText("It's too late to undo this.");
});
