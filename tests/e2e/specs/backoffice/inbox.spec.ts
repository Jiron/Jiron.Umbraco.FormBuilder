import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';

// The Inbox and Spam (ROADMAP 11.1 part c, IA 1, 6.1 and 6.2, ADR-0209). The viewer's Inbox holds no entry of the
// restricted folder's forms, nor offers them in the Form filter, and the Inbox's badge counts what the viewer may view;
// Not spam on an entry whose unique answer another entry holds by then asks first and moves only once confirmed; and the
// badge's poll never keeps an idle user signed in: once nobody uses the backoffice, it waits instead of renewing the
// access token, and Umbraco ends the session on time, 20 minutes after the last renewal (page.clock). Email me a
// summary... keeps the user's choice, shows the mail it sends and sends a test of it.

const SCREENS = '/umbraco/section/jfb-forms/workspace';

interface Seeded {
  readonly key: string;
  readonly name: string;
}

interface Row {
  readonly key: string;
  readonly number: number;
  readonly state: string;
}

/** A form of applications whose entries wait for approval. */
async function seed(api: APIRequestContext, name: string, more: Record<string, unknown> = {}): Promise<Seeded> {
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count: 3, name, url: `${STUB_URL}/approved/${randomUUID()}`, ...more },
    timeout: 120_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  return { key: ((await seeded.json()) as { key: string }).key, name };
}

async function entriesOf(api: APIRequestContext, form: string, states: string[] = []): Promise<Row[]> {
  const answer = await api.post(`${API}/form/${form}/entries`, { data: { filter: { states, fields: [] } } });
  expect(answer.ok(), await answer.text()).toBe(true);
  return ((await answer.json()) as { items: Row[] }).items;
}

/** Filters a list to one form through its Form filter, found by its name. */
async function filterToForm(page: Page, view: Locator, name: string): Promise<void> {
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'Form', exact: true }).click();
  const dialog = page.locator('jfb-entry-forms-modal');
  await dialog.locator('uui-input input').fill(name);
  await dialog.locator('uui-input input').press('Enter');
  await dialog.locator(`uui-checkbox[label="${name}"]`).click();
  await dialog.getByRole('button', { name: 'Apply filter', exact: true }).click();
  await expect(view.locator('.chip-text', { hasText: `Form: ${name}` })).toBeVisible();
}

test("the viewer's Inbox holds no entry of the restricted folder's forms, and its badge counts what the viewer may view", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const stamp = String(Date.now());
  const open = await seed(api, `Inbox open ${stamp}`);
  const hidden = await seed(api, `Inbox HR ${stamp}`, { count: 2 });

  // Into the restricted folder HR, which only the HR team may open.
  const root = await api.get(`${API}/tree/root?skip=0&take=500`);
  const hr = ((await root.json()) as { items: { key: string; name: string; kind: string }[] }).items.find(
    (item) => item.name === 'HR' && item.kind === 'folder',
  );
  expect(hr, 'the restricted folder HR').toBeTruthy();
  const form = (await (await api.get(`${API}/form/${hidden.key}`)).json()) as { concurrencyVersion: number };
  const moved = await api.put(`${API}/form/${hidden.key}/move`, {
    data: { concurrencyVersion: form.concurrencyVersion, folderKey: hr?.key, widenAccess: true },
  });
  expect(moved.ok(), await moved.text()).toBe(true);

  await loginAs('viewer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SCREENS}/jfb-inbox`);
  const view = page.locator('jfb-entries-view');
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });
  // Unseen first, newest first: the restricted form's entries, the newest of all, would lead the list.
  await expect(view.locator('td.form-column', { hasText: open.name })).toHaveCount(3);
  await expect(view.locator('td.form-column', { hasText: hidden.name })).toHaveCount(0);
  await axeInside(page, 'jfb-entries-view');

  // The Form filter finds no such form for the viewer.
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'Form', exact: true }).click();
  const dialog = page.locator('jfb-entry-forms-modal');
  await dialog.locator('uui-input input').fill(hidden.name);
  await dialog.locator('uui-input input').press('Enter');
  await expect(dialog.locator('[role="status"]')).toHaveText(
    `No form you may view has \u201c${hidden.name}\u201d in its name.`,
  );
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  // The badge says what the server counts for the viewer, and the item names it for screen readers.
  const viewerApi = await apiAs('viewer');
  const unseen = (await (await viewerApi.get(`${API}/entries/unseen`)).json()) as { count: number; more: boolean };
  const said = unseen.more ? `${String(unseen.count)}+` : String(unseen.count);
  const item = page.locator('jfb-inbox-menu-item uui-menu-item');
  await expect(item.locator('uui-badge')).toHaveText(said);
  await expect(item).toHaveAttribute('label', `Inbox, ${said} unseen`);
  await expect(item).toHaveAttribute('active', '');

  // The HR team sees the form's entries in its Inbox.
  const hrApi = await apiAs('hr');
  const hrInbox = await hrApi.post(`${API}/entries/inbox`, { data: { filter: { forms: [hidden.key] } } });
  expect(hrInbox.ok(), await hrInbox.text()).toBe(true);
  expect(((await hrInbox.json()) as { items: unknown[] }).items).toHaveLength(2);
});

test('Not spam on an entry whose unique answer another entry holds by then asks first, and moves only once confirmed', async ({
  page,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const seeded = await seed(api, `Spam unique ${String(Date.now())}`, { unique: true });
  const second = (await entriesOf(api, seeded.key)).find((row) => row.number === 2);
  expect(second, 'entry 2').toBeTruthy();
  const marked = await api.post(`${API}/form/${seeded.key}/entry-state`, {
    data: { entries: [second?.key], state: 'spam' },
  });
  expect(marked.ok(), await marked.text()).toBe(true);

  // Meanwhile a visitor sends the same name: an entry in Spam holds no answer, so the form takes it.
  const asked = await request.post(`/jfb/api/v1/forms/${seeded.key}/challenge`);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${seeded.key}/entries`, {
    data: { revision: 1, values: { name: 'Person 0002', position: 'p1', amount: '1', salary: '1' }, challenge },
  });
  expect(sent.status(), await sent.text()).toBe(200);

  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SCREENS}/jfb-spam`);
  const view = page.locator('jfb-entries-view');
  await expect(view.locator('tbody tr, .empty').first()).toBeVisible({ timeout: 60_000 });
  await filterToForm(page, view, seeded.name);
  const row = view.locator('tbody tr', { hasText: 'Person 0002' });
  await expect(row).toHaveCount(1);
  await expect(view.locator('thead th', { hasText: 'State' })).toHaveCount(0);
  await axeInside(page, 'jfb-entries-view');

  const notSpam = () => view.locator('.bulk').getByRole('button', { name: 'Not spam', exact: true }).click();
  await row.locator('uui-checkbox').click();
  await notSpam();
  const dialog = page.locator('umb-confirm-modal');
  await expect(dialog).toContainText('Move it back anyway?');
  await expect(dialog).toContainText('Another entry has the same Name, which must be unique.');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect((await entriesOf(api, seeded.key, ['spam'])).map((one) => one.number)).toEqual([2]);

  // Cancel keeps the selection; confirmed, the entry leaves Spam, and the row stays until the list reads again, saying
  // its state.
  await expect(view.locator('.bulk')).toContainText('1 selected');
  await notSpam();
  await page.locator('umb-confirm-modal').getByRole('button', { name: 'Move back anyway', exact: true }).click();
  await expect(row.locator('.moved-state')).toHaveText('Waiting for approval');
  await expect.poll(async () => (await entriesOf(api, seeded.key, ['spam'])).length).toBe(0);
});

test("the Inbox badge's poll never keeps an idle user signed in, and Umbraco ends the session on time (ADR-0209)", async ({
  browser,
  loginAs,
}) => {
  test.setTimeout(240_000);
  // A context of its own, whose page's clock jumps past the access token's end and then the session's.
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await page.clock.install();
    await loginAs('viewer', page);
    await page.goto('/umbraco/section/jfb-forms');
    await expect(page.locator('jfb-inbox-menu-item uui-menu-item')).toBeVisible({ timeout: 60_000 });
    let polls = 0;
    let renewals = 0;
    page.on('request', (request) => {
      if (request.url().includes('/jfb/management/api/v1/entries/unseen')) polls++;
      if (request.url().includes('/security/back-office/token')) renewals++;
    });

    // Opening the page counts as use: the first time the access token runs out (a quarter of Umbraco's 20 minutes), the
    // poll renews it, and with it the session, which now ends 20 minutes after that.
    await page.clock.fastForward('06:00');
    await expect.poll(() => renewals, { timeout: 45_000 }).toBeGreaterThan(0);
    await expect.poll(() => polls, { timeout: 45_000 }).toBeGreaterThan(0);

    // Nobody used the backoffice since: each time the token has run out, the poll waits instead of renewing it, while
    // the session lasts.
    await page.waitForTimeout(3_000);
    const waiting = { polls, renewals };
    for (const step of ['06:00', '06:00', '06:00']) {
      await page.clock.fastForward(step);
      await page.waitForTimeout(3_000);
    }
    expect({ polls, renewals }, 'no call while nobody uses the backoffice').toEqual(waiting);
    await expect(page.locator('umb-app-auth-modal')).toHaveCount(0);

    // So the session ends on time, 20 minutes after the last renewal: Umbraco times it out and asks the person to sign
    // in again (the jump passes its warning, so the warning's countdown never shows). Its own reconnect then tries the
    // session's refresh token, which a server 20 minutes on refuses; this one's clock is real, so it is told to.
    await page.route('**/security/back-office/token', (route) =>
      route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: 'invalid_grant' }) }),
    );
    await page.clock.fastForward('06:00');
    await expect(page.locator('umb-app-auth-modal')).toBeVisible({ timeout: 45_000 });
    expect(polls, 'the poll waited to the end').toBe(waiting.polls);
  } finally {
    await context.close();
  }
});

test('Email me a summary\u2026 keeps the choice, shows the mail it sends, and sends a test to the own address', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  // An entry the viewer may see, so the Inbox has its toolbar.
  await seed(await apiAs('admin'), `Summary ${String(Date.now())}`);
  await loginAs('viewer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SCREENS}/jfb-inbox`);
  const view = page.locator('jfb-entries-view');
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });

  const open = async () => {
    await view.locator('.toolbar').getByRole('button', { name: 'More', exact: true }).click();
    await view.locator('#more-menu').getByRole('button', { name: 'Email me a summary\u2026' }).click();
    const sidebar = page.locator('jfb-entry-digest-modal');
    await expect(sidebar.locator('uui-radio-group')).toBeVisible();
    return sidebar;
  };

  const sidebar = await open();
  await expect(sidebar.locator('#when')).toContainText("the time zone set for all sites, and covers the day or week before.");
  await sidebar.locator('uui-radio[value="weekly"]').click();
  // The weekly mail of the week before, in the site's layout, its address said once below it.
  await expect(sidebar.locator('jfb-email-preview .subject')).toContainText(' to ');
  await expect(sidebar.locator('jfb-email-preview iframe')).toBeVisible();
  await expect(sidebar.locator('jfb-email-preview .meta dt')).toHaveText(['From', 'Subject']);
  await expect(sidebar.locator('.note')).toContainText('It goes to your own address, ');
  await axeInside(page, 'jfb-entry-digest-modal');

  await sidebar.getByRole('button', { name: 'Send test email', exact: true }).click();
  await expect(page.locator('uui-toast-notification', { hasText: 'Test email sent to ' })).toBeVisible();
  await sidebar.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(
    page.locator('uui-toast-notification', { hasText: "You'll get a summary each Monday morning." }),
  ).toBeVisible();
  await expect(sidebar).toHaveCount(0);

  // Opened again, it holds the choice; Off turns it off.
  const again = await open();
  await expect
    .poll(async () => again.locator('uui-radio-group').evaluate((group) => (group as HTMLInputElement).value))
    .toBe('weekly');
  await again.locator('uui-radio[value="off"]').click();
  await again.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.locator('uui-toast-notification', { hasText: 'The summary email is off.' })).toBeVisible();
});
