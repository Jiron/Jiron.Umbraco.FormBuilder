import type { APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';
import { bodyOf, headerOf } from '../../support/mail.ts';
import { STUB_URL } from '../../support/paths.ts';

// The entry panel (ROADMAP 11.2, IA 6.3, ADR-0208). Approve in the panel keeps it on the entry, Undo puts the entry
// back, and the audit log and the timeline hold both; a rejected entry moved back onto a full option and a unique answer
// another entry holds asks first and overbooks only once confirmed (ADR-0090, ADR-0091); Forward... sends the entry to a
// team address, which the pickup folder receives, while the API refuses an address outside the allowed ones; and the
// {{ entry.url }} of a mail opens the panel alone, and gives a user without access the backoffice's "not found".

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Row {
  readonly key: string;
  readonly number: number;
  readonly state: string;
}

interface Seeded {
  readonly key: string;
  readonly alias: string;
}

/** A form of applications whose entries wait for approval, with what each test needs of it. */
async function seed(api: APIRequestContext, name: string, more: Record<string, unknown> = {}): Promise<Seeded> {
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count: 6, name, url: `${STUB_URL}/approved/${randomUUID()}`, ...more },
    timeout: 120_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  return (await seeded.json()) as Seeded;
}

/** The form's entries as the list's API answers them, newest first. */
async function entriesOf(api: APIRequestContext, form: string): Promise<Row[]> {
  const answer = await api.post(`${API}/form/${form}/entries`, {
    data: { filter: { states: [], fields: [] }, columns: [] },
  });
  expect(answer.ok(), await answer.text()).toBe(true);
  return ((await answer.json()) as { items: Row[] }).items;
}

const entryNumbered = async (api: APIRequestContext, form: string, number: number): Promise<Row> => {
  const found = (await entriesOf(api, form)).find((row) => row.number === number);
  expect(found, `entry ${String(number)}`).toBeTruthy();
  return found!;
};

test('Approve in the panel stays on the entry, Undo puts it back, and the audit log and the timeline hold both', async ({
  page,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const { key } = await seed(api, `Panel ${String(Date.now())}`);

  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/entries`);
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(6, { timeout: 60_000 });

  // The newest entry opens beside the list, which narrows to name, state and time; opening it marks it seen.
  await rows.first().locator('.name').click();
  const panel = view.locator('jfb-entry-panel');
  await expect(panel.locator('h2')).toHaveText('Entry #6');
  await expect(view).toHaveAttribute('split', '');
  await expect(rows.first()).toHaveAttribute('aria-current', 'true');
  await expect(rows.first()).not.toHaveClass(/unseen/);
  await expect(view.locator('th.runs-column').first()).toBeHidden();
  await axeInside(page, 'jfb-entries-view');

  const bar = panel.locator('footer.bar');
  await bar.getByRole('button', { name: 'Approve', exact: true }).click();
  const toast = page.locator('jfb-undo-notification').last();
  await expect(toast).toContainText('Entry #6 approved.');
  await expect(rows.first().locator('uui-tag')).toHaveText('Approved');
  // The panel stays on the entry, with the actions of its new state.
  await expect(panel.locator('h2')).toHaveText('Entry #6');
  await expect(bar.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
  await expect(bar.getByRole('button', { name: 'Mark as spam', exact: true })).toBeVisible();

  await toast.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(rows.first().locator('uui-tag')).toHaveText('Waiting for approval');
  await expect(bar.getByRole('button', { name: 'Approve', exact: true })).toBeVisible();
  const sixth = await entryNumbered(api, key, 6);
  expect(sixth.state).toBe('submitted');

  // The audit log holds the change and its undo, by the user, for this entry.
  const audit = await request.get(`/jfb-testing/audit?entity=${key}`);
  expect(audit.ok(), await audit.text()).toBe(true);
  const recorded = ((await audit.json()) as { entries: { action: string; user: string | null; details: unknown }[] })
    .entries;
  const aboutSixth = recorded.filter((entry) => JSON.stringify(entry.details).includes(sixth.key));
  expect(aboutSixth.map((entry) => entry.action)).toEqual(['entry.stateChanged', 'entry.stateChangeUndone']);
  expect(aboutSixth.every((entry) => entry.user !== null)).toBe(true);

  // The timeline says who opened it, who approved it and who undid that.
  await panel.getByRole('tab', { name: 'Timeline', exact: true }).click();
  const events = panel.locator('.events');
  await expect(events).toContainText('Opened by Eli Entries');
  await expect(events).toContainText('Change undone by Eli Entries');

  // j reads on: the panel follows the cursor to the next entry, and a approves the open one.
  await rows.first().focus();
  await page.keyboard.press('j');
  await expect(panel.locator('h2')).toHaveText('Entry #5');
  await page.keyboard.press('a');
  await expect(page.locator('jfb-undo-notification').last()).toContainText('Entry #5 approved.');
  await expect(rows.nth(1).locator('uui-tag')).toHaveText('Approved');

  // Esc closes the panel and gives the list its width back.
  await page.keyboard.press('Escape');
  await expect(panel).toHaveCount(0);
  await expect(view).not.toHaveAttribute('split', '');
});

test('a rejected entry moved back onto a full option and a unique answer held asks first, and overbooks only once confirmed', async ({
  page,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  // Entries 2 and 6 are the accountants, whose position holds 2 places; names are unique.
  const { key } = await seed(api, `Places ${String(Date.now())}`, { places: 2, unique: true });

  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/entries`);
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(6, { timeout: 60_000 });
  const second = rows.filter({ hasText: 'Person 0002' });
  await second.locator('.name').click();
  const panel = view.locator('jfb-entry-panel');
  await expect(panel.locator('h2')).toHaveText('Entry #2');
  const bar = panel.locator('footer.bar');
  await bar.getByRole('button', { name: 'Reject', exact: true }).click();
  await expect(second.locator('uui-tag')).toHaveText('Rejected');
  // The toast sits over the bar until it goes; a pointer resting on it would hold it (UUI pauses a hovered toast).
  await page.mouse.move(400, 400);
  await expect(page.locator('jfb-undo-notification')).toHaveCount(0, { timeout: 30_000 });

  // Meanwhile a visitor takes the freed place, under the same name.
  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision: 1, values: { name: 'Person 0002', position: 'p3', amount: '1', salary: '1' }, challenge },
  });
  expect(sent.status(), await sent.text()).toBe(200);

  // Moving the rejected entry back asks first, with both reasons; Cancel moves nothing. An entry handler may neither
  // delete nor re-run, so the bar shows Back to waiting for approval among its first three.
  const back = () => bar.getByRole('button', { name: 'Back to waiting for approval', exact: true }).click();
  await back();
  const dialog = page.locator('umb-confirm-modal');
  await expect(dialog).toContainText('Move it back anyway?');
  await expect(dialog).toContainText('Accountant (Position) is full by now: all 2 places are taken.');
  await expect(dialog).toContainText('Another entry has the same Name, which must be unique.');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect((await entryNumbered(api, key, 2)).state).toBe('rejected');

  // Confirmed, it moves back, though the position is overbooked by then.
  await back();
  await page.locator('umb-confirm-modal').getByRole('button', { name: 'Move back anyway', exact: true }).click();
  await expect(second.locator('uui-tag')).toHaveText('Waiting for approval');
  await expect.poll(async () => (await entryNumbered(api, key, 2)).state).toBe('submitted');
});

test('Forward... reaches a team address, the API refuses one outside, and the mail link opens the panel only for who may see it', async ({
  page,
  browser,
  loginAs,
  apiAs,
  waitForMail,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const name = `Forwards ${String(Date.now())}`;
  const notice = `notice-${randomUUID()}@jfb.test`;
  const team = 'e2e-team@jfb.test';
  const { key } = await seed(api, name, { notice, forward: team });

  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/entries`);
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(6, { timeout: 60_000 });
  await rows.filter({ hasText: 'Person 0003' }).locator('.name').click();
  const panel = view.locator('jfb-entry-panel');
  await expect(panel.locator('h2')).toHaveText('Entry #3');

  await panel.locator('footer.bar').getByRole('button', { name: 'Forward\u2026', exact: true }).click();
  const sidebar = page.locator('jfb-entry-forward-modal');
  await expect(sidebar.locator('select#to')).toBeVisible();
  // Without viewSensitive, the sidebar offers no sensitive values.
  await expect(sidebar.locator('uui-checkbox')).toHaveCount(0);
  await sidebar.locator('select#to').selectOption(`address:${team}`);
  await sidebar.locator('uui-textarea#message textarea').fill('Please call back.');
  await sidebar.getByRole('button', { name: 'Forward', exact: true }).click();
  await expect(page.locator('uui-toast-notification').last()).toContainText(`Entry forwarded to ${team}.`);

  // The subject names the entry by its title, then its number and form (a middle dot between).
  const subject = `Person 0003 ${String.fromCharCode(0xb7)} Entry #3 of ${name}`;
  const forwarded = await waitForMail(
    (mail) => headerOf(mail.raw, 'To').includes(team) && headerOf(mail.raw, 'Subject') === subject,
    60_000,
  );
  const forwardedBody = bodyOf(forwarded.raw, 'text/html');
  expect(forwardedBody).toContain('Please call back.');
  expect(forwardedBody).toContain('Person 0003');
  // The salary is sensitive, and the user didn't send it.
  expect(forwardedBody).not.toContain('30003');

  // An address outside the users and team addresses is refused by the API itself.
  const third = await entryNumbered(api, key, 3);
  const handler = await apiAs('entryHandler');
  const refused = await handler.post(`${API}/form/${key}/entry/${third.key}/forward`, {
    data: { address: 'stranger@example.org', message: null, includeSensitive: false },
  });
  expect(refused.status()).toBe(400);
  expect(((await refused.json()) as { code: string }).code).toBe('forward_recipient_not_allowed');

  // The notice of entry 1 links to it; the link opens the panel alone, with the entry's title and its form.
  const first = await entryNumbered(api, key, 1);
  const mail = await waitForMail(
    (each) => headerOf(each.raw, 'To').includes(notice) && headerOf(each.raw, 'Subject') === 'Entry 1',
    60_000,
  );
  const href = /(https?:\/\/[^\s"<]+\/jfb-entry\/edit\/[0-9a-f-]{36})/.exec(bodyOf(mail.raw, 'text/html'))?.[1];
  expect(href, 'the mail holds the entry link').toBeTruthy();
  const link = new URL(href!);
  expect(link.pathname).toBe(`/umbraco/section/jfb-forms/workspace/jfb-entry/edit/${first.key}`);
  await page.goto(link.pathname);
  const alone = page.locator('jfb-entry-screen jfb-entry-panel');
  await expect(alone.locator('h2')).toHaveText('Person 0001 \u00b7 Entry #1', { timeout: 60_000 });
  await expect(alone.locator('a.form-link')).toHaveText(name);
  await expect(alone.locator('.head uui-tag')).toHaveText('Waiting for approval');
  await axeInside(page, 'jfb-entry-screen');

  // The same link, for a user without access to the form, gets the backoffice's "not found", and nothing of the entry.
  const elsewhere = await browser.newContext();
  try {
    const other = await elsewhere.newPage();
    await loginAs('noAccess', other);
    await other.goto(link.pathname);
    const screen = other.locator('jfb-entry-screen');
    await expect(screen.locator('umb-entity-detail-not-found')).toContainText('entry not found', { timeout: 60_000 });
    await expect(screen.locator('jfb-entry-panel')).toHaveCount(0);
    await expect(other.locator('body')).not.toContainText('Person 0001');
  } finally {
    await elsewhere.close();
  }
});
