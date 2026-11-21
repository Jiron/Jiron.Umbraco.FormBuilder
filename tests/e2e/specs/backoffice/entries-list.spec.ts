import type { APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, headlessPersonForm, sendAsVisitor, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';

// A form's entries list (ROADMAP 11.1, IA 6.1, ADR-0207), with 1,000 entries sent through the submission pipeline that
// wait for approval, and a webhook on approval. The list counts them and pages 50 at a time; a search finds one by
// its name and by its number; a filter set saved here opens in a second browser; a bulk approval is undone from its
// toast inside the window, every entry back in its state and no approval's webhook sent, and once the window has
// passed the toast offers no Undo (ADR-0023). The Actions column speaks only where an entry's action did not go out,
// and a filter's dialog names its boxes by its headline alone (ROADMAP 22.4).

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

  // Another approval: once its window has passed, its toast offers no Undo and says it is too late.
  await rows.nth(3).locator('uui-checkbox').click();
  await view.locator('.bulk').getByRole('button', { name: 'Approve', exact: true }).click();
  const undo = toast.getByRole('button', { name: 'Undo', exact: true });
  await expect(undo).toBeVisible();
  // The window ends 10 s after the approval by the server's clock, however late its answer reached the page, and the
  // toast stays 6 s past it: the clock moves a second at a time until the Undo goes, so the toast is read while it stands.
  for (let second = 0; second < 11 && (await undo.count()) > 0; second++) await page.clock.fastForward(1_000);
  await expect(undo).toHaveCount(0);
  await expect(toast).toContainText("It's too late to undo this.");

  // Past the windows on the server, that approval's webhook goes and the undone ones' never do. The host's clock is every
  // spec's, so the windows pass in real time: once the later approval's call has come, the earlier window is long over.
  await expect.poll(() => approvalCalls(api, hook), { timeout: 60_000 }).toBe(1);
  await page.waitForTimeout(2_000);
  expect(await approvalCalls(api, hook)).toBe(1);
});

/** Gives a published headless form one webhook on each entry, to this address, and publishes it. */
async function webhookTo(api: APIRequestContext, form: string, url: string): Promise<void> {
  const draft = (await (await api.get(`${API}/form/${form}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown>;
  };
  const action = { key: randomUUID(), type: 'webhook', name: 'Tell the CRM', enabled: true, trigger: 'submitted' };
  const saved = await api.put(`${API}/form/${form}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: { ...draft.definition, actions: [{ ...action, stopOnFailure: false, settings: { url } }] },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const published = await api.post(`${API}/form/${form}/publish`, {
    data: { concurrencyVersion: (await formOf(api, form)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

test('the Actions column speaks only where an action did not go out, and a filter names its boxes once', async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const form = await headlessPersonForm(api, unique('Runs said'));
  // The first entry's webhook goes through; the stub knows no second address, so the second entry's fails (404).
  const hook = `/webhooks/${randomUUID()}`;
  await webhookTo(api, form, `${STUB_URL}${hook}`);
  await sendAsVisitor(request, form, { name: 'Ada Visitor', email: 'ada@example.com' });
  await expect.poll(() => approvalCalls(api, hook), { timeout: 90_000 }).toBe(1);
  await webhookTo(api, form, `${STUB_URL}/nowhere/${randomUUID()}`);
  await sendAsVisitor(request, form, { name: 'Bea Visitor', email: 'bea@example.com' });

  await loginAs('admin');
  await page.setViewportSize({ width: 1440, height: 900 });
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(async () => {
    await page.goto(`${SECTION}/${form}/view/entries`);
    await expect(rows).toHaveCount(2, { timeout: 30_000 });
    await expect(rows.first().locator('td.runs')).toHaveText('1 failed', { timeout: 5_000 });
  }).toPass({ timeout: 120_000 });
  // The column keeps its header; the entry whose action went out says nothing in it (ROADMAP 22.4).
  await expect(view.locator('th.runs-column')).toHaveText('Actions');
  await expect(rows.nth(1).locator('td.runs')).toHaveText('');
  await axeInside(page, 'jfb-entries-view');

  // The Actions filter's dialog: its headline names its choices, and nothing names them again.
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'Actions', exact: true }).click();
  const filter = page.locator('jfb-entry-filter-modal');
  await expect(filter.getByRole('group', { name: 'Filter by actions', exact: true })).toBeVisible();
  await expect(filter.locator('legend')).toHaveCount(0);
  await expect(filter.getByText('Actions', { exact: true })).toHaveCount(0);
  await filter.locator('uui-radio[label="Failed"]').click();
  await filter.getByRole('button', { name: 'Apply filter', exact: true }).click();
  await expect(rows).toHaveCount(1);
  await expect(rows.first().locator('td.runs')).toHaveText('1 failed');
});
