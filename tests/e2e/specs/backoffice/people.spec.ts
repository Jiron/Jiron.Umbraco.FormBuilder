import { readFileSync } from 'node:fs';
import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, headlessPersonForm, sendAsVisitor, unique } from '../../support/forms-api.ts';

// Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): a privacy officer finds a person's two entries by their
// address, puts one on legal hold from the sidebar, exports everything as a ZIP from the ready toast, and erases: the
// dialog says what goes and what the hold keeps, with Cancel focused, and the held entry stays. Released, the last entry
// is anonymised, and a search for the address finds nothing more, while another person's entry on the form stays.

const SECTION = '/umbraco/section/jfb-forms';

/** A published headless form with a name and an email field; its key. */
const seed = headlessPersonForm;

/** Sends the form as a visitor would, through the public API. */
const send = (request: APIRequestContext, key: string, name: string, email: string) =>
  sendAsVisitor(request, key, { name, email });

/** The entries the People search finds for an address. */
const found = async (api: APIRequestContext, email: string) =>
  ((await (await api.post(`${API}/people/find`, { data: { email } })).json()) as { entries: { number: number }[] })
    .entries;

/** A day as a date box takes it, so many days from now, in UTC. */
const dayAhead = (days: number) => new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

test('a privacy officer finds a person, holds, exports and erases their entries, and the hold keeps one until released', async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const admin = await apiAs('admin');
  const stamp = Date.now().toString(36);
  const who = `ada-${stamp}@example.com`;
  const other = `grace-${stamp}@example.com`;
  const formName = unique('People');
  const key = await seed(admin, formName);
  await send(request, key, 'Ada Lovelace', who);
  await send(request, key, 'Ada Lovelace', who);
  await send(request, key, 'Grace Hopper', other);

  await loginAs('privacyOfficer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(SECTION);
  await page.locator('uui-menu-item[label="People"]').first().click();
  const screen = page.locator('jfb-people-screen');
  const email = screen.getByLabel('Email address', { exact: true });
  await expect(email).toBeVisible({ timeout: 60_000 });
  await axeInside(page, 'jfb-people-screen');

  // Something that is no address is said under the box, and nothing is searched.
  await email.fill('ada at example');
  await screen.getByRole('button', { name: 'Find', exact: true }).click();
  await expect(screen.locator('#email-why')).toHaveText('Enter one email address, such as name@example.com.');
  await expect(email).toHaveAttribute('aria-invalid', 'true');

  // Found in whatever case it is typed.
  const find = async () => {
    await email.fill(who.toUpperCase());
    await screen.getByRole('button', { name: 'Find', exact: true }).click();
  };
  await find();
  const results = screen.locator('uui-box[headline="2 entries in 1 form"]');
  await expect(results).toBeVisible({ timeout: 60_000 });
  await expect(results.locator('tbody tr')).toHaveCount(2);
  await axeInside(page, 'jfb-people-screen');

  // A legal hold on the first entry, from the sidebar.
  await screen.getByRole('button', { name: 'Set legal hold\u2026', exact: true }).click();
  const sidebar = page.locator('jfb-people-hold-modal');
  await expect(sidebar.getByRole('heading', { name: 'Set legal hold' })).toBeVisible();
  await sidebar.getByRole('button', { name: 'Set legal hold', exact: true }).click();
  await expect(sidebar.getByText('Choose at least one entry.')).toBeVisible();
  await sidebar.locator('label.pick', { hasText: 'Entry #1' }).locator('input').check();
  await sidebar.getByLabel('Reason', { exact: true }).fill('Complaint 2026-17');
  await sidebar.getByLabel('Keep until', { exact: true }).fill(dayAhead(30));
  await axeInside(page, 'jfb-people-hold-modal');
  await sidebar.getByRole('button', { name: 'Set legal hold on 1 entry', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(
    page.locator('uui-toast-notification').filter({ hasText: '1 entry on legal hold.' }).last(),
  ).toBeVisible();
  const first = results.locator('tbody tr', { hasText: 'Entry #1' });
  await expect(first.locator('uui-tag')).toHaveText(/^Legal hold until /);

  // Everything, as a ZIP from the ready toast, saved through a link with a ticket.
  await screen.getByRole('button', { name: 'Export everything', exact: true }).click();
  const ready = page.locator('jfb-action-notification').filter({ hasText: 'Your export of 2 entries is ready.' });
  await expect(ready).toBeVisible({ timeout: 90_000 });
  const downloading = page.waitForEvent('download');
  await ready.getByRole('button', { name: 'Download', exact: true }).click();
  const download = await downloading;
  expect(download.url()).toMatch(/\/people\/export\/[0-9a-f-]{36}\/file\?ticket=[\w-]+$/);
  expect(download.suggestedFilename()).toMatch(/^personal-data-\d{4}-\d{2}-\d{2}\.zip$/);
  const zip = readFileSync(await download.path());
  expect(zip.subarray(0, 2).toString('latin1'), 'a ZIP file').toBe('PK');
  expect(zip.toString('latin1').split('/entry.json').length - 1, "each entry's file, in its folder and the index").toBe(
    4,
  );

  // Erase: the dialog says what goes and what the hold keeps, with Cancel focused.
  await screen.getByRole('button', { name: 'Erase\u2026', exact: true }).click();
  const dialog = page.locator('jfb-people-erase-modal');
  // A lone form is named in the sentence, and no row says its count again (16.6's review).
  await expect(dialog.locator('#what')).toHaveText(
    `1 entry in ${formName} is erased. Insights stay. You can't undo this.`,
  );
  await expect(dialog.locator('.forms')).toHaveCount(0);
  await expect(dialog.getByText('1 entry on legal hold stays until its hold ends.')).toBeVisible();
  await expect(dialog.locator('#cancel')).toBeFocused();
  await axeInside(page, 'jfb-people-erase-modal');
  await dialog.getByRole('button', { name: 'Erase 1 entry', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(screen.locator('.done')).toHaveText(
    new RegExp(
      `Everything about ${who.toUpperCase().replace(/[.]/g, '\\.')} is erased: 1 entry\\. Insights stay\\. ` +
        "1 entry on legal hold stays until its hold ends\\.\\s*It's recorded in the audit log\\.",
    ),
  );
  expect((await found(admin, who)).map((entry) => entry.number)).toEqual([1]);

  // Released, the last entry is anonymised: it stays in the counts, and the address finds nothing more.
  await find();
  const left = screen.locator('uui-box[headline="1 entry in 1 form"]');
  await expect(left).toBeVisible();
  await left.getByRole('button', { name: 'Release the legal hold on Entry #1', exact: true }).click();
  await expect(
    page.locator('uui-toast-notification').filter({ hasText: 'The legal hold is released.' }).last(),
  ).toBeVisible();
  await expect(left.locator('uui-tag')).toHaveCount(0);
  await screen.getByRole('button', { name: 'Erase\u2026', exact: true }).click();
  await dialog.getByRole('radio', { name: 'Anonymise them' }).check();
  await dialog.getByRole('button', { name: 'Anonymise 1 entry', exact: true }).click();
  await expect(screen.locator('.done p').first()).toHaveText(
    /1 entry anonymised, which stays in the counts\. Insights stay\./,
  );
  await find();
  await expect(screen.getByText(`No entries found for ${who.toUpperCase()}.`, { exact: false })).toBeVisible();
  expect(await found(admin, who)).toEqual([]);
  expect(
    (await found(admin, other)).map((entry) => entry.number),
    "the other person's entry stays",
  ).toEqual([3]);
});
