import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { API, contactForm, headlessPersonForm, sendAsVisitor, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';
import { addFieldOf, pickerClosed, pickerItem, pickerSearch } from '../../support/picker.ts';
import { ClickCounter, expectTaskClicks } from '../../support/top-tasks.ts';
import { shownRow } from '../../support/tree.ts';
import { goToPlace, goToTab, type Clicker } from '../../support/places.ts';

// The top tasks on the built screens (ROADMAP 16.6, docs/ux/top-tasks.md): the first five tasks of the designer and
// the two that the designer's redraw changed (7 and 10, ROADMAP 22.1), the
// entry handler, the viewer and the privacy officer, and the three of a user without grants, each done with the mouse
// from the section's start page at 1440 px, and each path's clicks counted as the document counts them and held to the
// document's number. The administrator's tasks change what the whole site reads, so the site-wide specs that change
// and restore those settings count theirs (settings.spec.ts, access.spec.ts), and the setup checklist's own host counts
// the checklist's (setup-checklist.spec.ts).

const SECTION = '/umbraco/section/jfb-forms';

/** The section's start page, the Overview, where every task starts. */
async function start(page: Page): Promise<void> {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(SECTION);
  await expect(page.locator('umb-section-sidebar')).toBeVisible({ timeout: 60_000 });
  await expect(page.locator('jfb-overview uui-box').first()).toBeVisible({ timeout: 60_000 });
}

/** Counts each step of the way to a place's tab as one click. */
const counted =
  (clicks: ClickCounter): Clicker =>
  (target) =>
    clicks.click(target);

/** A form of applications whose entries wait for approval, at the root, as the entry tasks' usual setup has it. */
async function waitingEntries(api: APIRequestContext, name: string, count = 6): Promise<void> {
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count, name, url: `${STUB_URL}/approved/${randomUUID()}` },
    timeout: 120_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
}

/** The entries list, its rows of one form, and the open entry's panel. */
function entriesOf(page: Page, form: string) {
  const view = page.locator('jfb-entries-view');
  return {
    view,
    rows: view.locator('tbody tr').filter({ has: page.locator('td.form-column', { hasText: form }) }),
    waiting: view
      .locator('tbody tr')
      .filter({ has: page.locator('td.form-column', { hasText: form }) })
      .filter({ hasText: 'Waiting for approval' }),
    panel: view.locator('jfb-entry-panel'),
  };
}

/** The open entry's own bar. */
const barOf = (panel: Locator) => panel.locator('footer.bar');

test('the designer starts forms, adds a field, rewords a question and makes it compulsory in the clicks the document says', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  await loginAs('designer');
  const createOption = (name: string) =>
    page.locator(`umb-entity-create-option-action-list-modal umb-ref-item[name="${name}"]`);
  const sidebarCreate = page.locator('umb-section-sidebar').getByRole('button', { name: 'Create' }).first();
  const design = page.locator('jfb-form-design-view');

  // 1. A new, empty form: its workspace opens on Design with the empty group's sentence.
  await start(page);
  let clicks = new ClickCounter();
  await clicks.click(sidebarCreate);
  await clicks.click(createOption('Form'));
  await expect(design).toContainText('Add your first field', { timeout: 60_000 });
  expectTaskClicks(clicks, 'Designer', 1);

  // 2. A form from the contact template, which the gallery has chosen already.
  await start(page);
  clicks = new ClickCounter();
  await clicks.click(sidebarCreate);
  await clicks.click(createOption('Form from a template\u2026'));
  const gallery = page.locator('jfb-template-gallery-modal');
  await expect(gallery.getByRole('radio', { name: /^Contact/ })).toHaveAttribute('aria-checked', 'true');
  await gallery.getByRole('button', { name: 'Use template' }).click();
  await expect(page).toHaveURL(/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/, { timeout: 60_000 });
  expectTaskClicks(clicks, 'Designer', 2);

  // 3. An email field, through the field picker from Add field, at the end of the form.
  const name = unique('Top designer');
  await contactForm(api, name);
  await start(page);
  clicks = new ClickCounter();
  // The tree is read as far as the form before the clicks are counted: on a site the form is on the tree's first page,
  // and the shared host's root holds every test's forms (shownRow).
  await clicks.click(await shownRow(page, name));
  const fields = design.locator('jfb-canvas-field');
  await expect(fields).toHaveCount(3, { timeout: 60_000 });
  await clicks.click(addFieldOf(design));
  await expect(pickerSearch(page)).toBeFocused();
  await clicks.click(pickerItem(page, 'Email'));
  await pickerClosed(page);
  await expect(fields).toHaveCount(4);
  expectTaskClicks(clicks, 'Designer', 3);

  // 4. A question reworded: selected on the canvas, its label typed in the inspector.
  await start(page);
  clicks = new ClickCounter();
  await clicks.click(await shownRow(page, name));
  await clicks.click(fields.first().locator('[data-control="select"]').first());
  const inspector = design.locator('jfb-designer-inspector');
  const label = inspector.getByLabel('Label', { exact: true });
  await label.fill('Your full name');
  await expect(fields.first()).toContainText('Your full name');
  expectTaskClicks(clicks, 'Designer', 4);

  // 5. A question made compulsory: selected, its Required switched on.
  await start(page);
  clicks = new ClickCounter();
  await clicks.click(await shownRow(page, name));
  await clicks.click(fields.nth(2).locator('[data-control="select"]').first());
  const required = inspector.getByRole('switch', { name: 'Required', exact: true });
  await required.check();
  await expect(required).toBeChecked();
  expectTaskClicks(clicks, 'Designer', 5);
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(page.locator('jfb-form-workspace-editor uui-tag')).toHaveText('Draft');

  // 7. The form split into two steps: the field the new page follows, its "...", Move what follows to a new page.
  await start(page);
  clicks = new ClickCounter();
  await clicks.click(await shownRow(page, name));
  await clicks.click(fields.nth(1).locator('[data-control="select"]').first());
  await clicks.click(fields.nth(1).locator('.bo-tools').getByRole('button', { name: 'More actions' }));
  await clicks.click(
    design.locator('.bo-tools .bo-menu').getByRole('button', { name: 'Move what follows to a new page' }),
  );
  await expect(
    design.getByRole('navigation', { name: 'Pages' }).getByRole('button', { name: 'Page 2', exact: true }),
  ).toHaveAttribute('aria-current', 'step');
  expectTaskClicks(clicks, 'Designer', 7);

  // 10. What visitors read after they send: After sending, the outcome's row, its message in the sidebar, Update.
  await start(page);
  clicks = new ClickCounter();
  await clicks.click(await shownRow(page, name));
  await expect(fields.first()).toBeVisible({ timeout: 60_000 });
  await clicks.click(design.getByRole('navigation', { name: 'Pages' }).getByRole('button', { name: 'After sending' }));
  await clicks.click(design.locator('jfb-form-outcomes jfb-outcome-list .row-open').first());
  const outcome = page.locator('jfb-outcome-modal');
  await expect(outcome).toBeVisible();
  await outcome.locator('#message [contenteditable="true"]').fill('Thank you. We answer within a day.');
  await outcome.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(outcome).toHaveCount(0);
  expectTaskClicks(clicks, 'Designer', 10);
});

test('the entry handler opens the Inbox, reads, approves, rejects and moves an entry to Spam in the clicks the document says', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const form = unique('Top inbox');
  await waitingEntries(await apiAs('admin'), form);
  await loginAs('entryHandler');
  const { view, rows, waiting, panel } = entriesOf(page, form);

  // 1. Everything new, from all forms.
  await start(page);
  let clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  await expect(rows).toHaveCount(6, { timeout: 60_000 });
  expectTaskClicks(clicks, 'Entry handler', 1);

  // 2. One entry read, beside the list.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  await clicks.click(rows.first().locator('.name'));
  await expect(panel.locator('h2')).toBeVisible();
  expectTaskClicks(clicks, 'Entry handler', 2);

  // 3 to 5. Approve, Reject and Mark as spam, each on an entry still waiting, from the panel's bar.
  const outcomes = [
    { number: 3, button: 'Approve', said: /approved\./ },
    { number: 4, button: 'Reject', said: /rejected\./ },
    { number: 5, button: 'Mark as spam', said: /spam/i },
  ] as const;
  for (const outcome of outcomes) {
    await start(page);
    clicks = new ClickCounter();
    await goToPlace(page, 'Inbox', counted(clicks));
    await expect(waiting.first()).toBeVisible({ timeout: 60_000 });
    await clicks.click(waiting.first().locator('.name'));
    await expect(panel.locator('h2')).toBeVisible();
    await clicks.click(barOf(panel).getByRole('button', { name: outcome.button, exact: true }));
    await expect(page.locator('jfb-undo-notification').last()).toContainText(outcome.said);
    expectTaskClicks(clicks, 'Entry handler', outcome.number);
    await expect(view).toBeVisible();
  }
});

test('the viewer sees recent entries, reads one, finds one by address, filters by month and uses a saved set in the clicks the document says', async ({
  page,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(300_000);
  const admin = await apiAs('admin');
  const form = unique('Top viewer');
  const key = await headlessPersonForm(admin, form);
  const address = `vera-${Date.now().toString(36)}@example.com`;
  await sendAsVisitor(request, key, { name: 'Vera Lindqvist', email: address });
  await sendAsVisitor(request, key, { name: 'Anton Berg', email: `anton-${Date.now().toString(36)}@example.com` });
  await loginAs('viewer');
  const { view, rows, panel } = entriesOf(page, form);

  // 1. What came in lately, on the Overview itself.
  await start(page);
  let clicks = new ClickCounter();
  const recent = page.locator('jfb-overview uui-box', { has: page.locator('.title', { hasText: 'Recent entries' }) });
  await expect(recent.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });
  expectTaskClicks(clicks, 'Viewer', 1);

  // 2. One entry read.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  await clicks.click(rows.first().locator('.name'));
  await expect(panel.locator('h2')).toBeVisible();
  expectTaskClicks(clicks, 'Viewer', 2);

  // 3. An entry found by an address: the search is the task's own input.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  const search = view.locator('uui-input.search input');
  await search.fill(address);
  await search.press('Enter');
  await expect(rows).toHaveCount(1, { timeout: 60_000 });
  await clicks.click(rows.first().locator('.name'));
  await expect(panel).toContainText('Vera Lindqvist');
  expectTaskClicks(clicks, 'Viewer', 3);

  // 4. Last month's entries: Filter, Received, then the month and Apply, which are the task's own.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });
  await clicks.click(view.getByRole('button', { name: 'Filter', exact: true }));
  await clicks.click(view.locator('#filter-menu').getByRole('button', { name: 'Received', exact: true }));
  const filter = page.locator('jfb-entry-filter-modal');
  await filter.locator('uui-radio[value="lastMonth"]').click();
  await filter.getByRole('button', { name: 'Apply filter', exact: true }).click();
  await expect(view.locator('.chip-text')).toHaveText([/Last month/]);
  expectTaskClicks(clicks, 'Viewer', 4);

  // 5. Yesterday's saved set, from the Filter menu: the set is the task's own choice.
  const setName = unique('This form');
  await saveFormSet(page, view, form, setName);
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Inbox', counted(clicks));
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });
  await clicks.click(view.getByRole('button', { name: 'Filter', exact: true }));
  // In the Inbox a set of one form names its form under its own name, which starts the button's name.
  await view
    .locator('#filter-menu')
    .getByRole('button', { name: new RegExp(`^${setName} `) })
    .click();
  // Filtered to the one form, the Inbox leaves its form column out: the chip names it, and its two entries show.
  await expect(view.locator('.chip-text')).toHaveText([new RegExp(form)]);
  await expect(view.locator('tbody tr')).toHaveCount(2, { timeout: 60_000 });
  expectTaskClicks(clicks, 'Viewer', 5);
});

/** Saves a filter set of one form's entries for the signed-in user, as the viewer did yesterday. */
async function saveFormSet(page: Page, view: Locator, form: string, setName: string): Promise<void> {
  await start(page);
  await goToPlace(page, 'Inbox');
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'Form', exact: true }).click();
  const forms = page.locator('jfb-entry-forms-modal');
  await forms.locator('uui-input input').fill(form);
  await forms.locator('uui-input input').press('Enter');
  await forms.locator('uui-checkbox', { hasText: form }).click();
  await forms.locator('uui-button[look="primary"]').click();
  await expect(view.locator('.chip-text')).toHaveText([new RegExp(form)]);
  await view.getByRole('button', { name: 'Save as\u2026', exact: true }).click();
  const naming = page.locator('jfb-entry-set-name-modal');
  await naming.locator('uui-input input').fill(setName);
  await naming.getByRole('button', { name: 'Save filter set', exact: true }).click();
  await expect(naming).toHaveCount(0);
}

test('the privacy officer finds a person and a member, exports and erases, and sees retention in the clicks the document says', async ({
  page,
  loginAs,
  apiAs,
  request,
  memberOf,
}) => {
  test.setTimeout(300_000);
  const admin = await apiAs('admin');
  const key = await headlessPersonForm(admin, unique('Top privacy'));
  const address = `pia-${Date.now().toString(36)}@example.com`;
  await sendAsVisitor(request, key, { name: 'Pia Persson', email: address });
  // A website member's own answers, under the member's address.
  await sendAsVisitor(request, key, { name: memberOf(0).name, email: memberOf(0).email });
  await loginAs('privacyOfficer');
  const screen = page.locator('jfb-people-screen');
  const results = screen.locator('uui-box', { hasText: /\d+ entr(y|ies) in \d+ forms?/ });
  const find = async (who = address) => {
    await screen.getByLabel('Email address', { exact: true }).fill(who);
    await screen.getByRole('button', { name: 'Find', exact: true }).click();
    await expect(results).toBeVisible({ timeout: 60_000 });
  };

  // 1. Everything stored about a person, by address: the address and Find are the task's own.
  await start(page);
  let clicks = new ClickCounter();
  await goToPlace(page, 'Privacy', counted(clicks));
  await find();
  expectTaskClicks(clicks, 'Privacy officer', 1);

  // 2. Everything stored about a website member: a privacy officer without Umbraco's Members section finds them by the
  // member's address, which finds the member's entries too (ADR-0223); Umbraco's member picker is not offered, as
  // Umbraco lists members only to who may open Members (ADR-0241).
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Privacy', counted(clicks));
  await expect(screen.getByRole('button', { name: /^Find a member instead/ })).toHaveCount(0);
  await find(memberOf(0).email);
  expectTaskClicks(clicks, 'Privacy officer', 2);

  // 3. A copy of their data: Export everything, then the ready toast's Download.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Privacy', counted(clicks));
  await find();
  await clicks.click(screen.getByRole('button', { name: 'Export everything', exact: true }));
  const ready = page
    .locator('jfb-action-notification')
    .filter({ hasText: /Your export of \d+ entr(y|ies) is ready\./ });
  await expect(ready).toBeVisible({ timeout: 90_000 });
  const downloading = page.waitForEvent('download');
  await clicks.click(ready.getByRole('button', { name: 'Download', exact: true }));
  expect((await downloading).suggestedFilename()).toMatch(/^personal-data-\d{4}-\d{2}-\d{2}\.zip$/);
  expectTaskClicks(clicks, 'Privacy officer', 3);

  // 4. Their data erased: Erase..., then the dialog's confirming button.
  await start(page);
  clicks = new ClickCounter();
  await goToPlace(page, 'Privacy', counted(clicks));
  await find();
  await clicks.click(screen.getByRole('button', { name: 'Erase\u2026', exact: true }));
  const dialog = page.locator('jfb-people-erase-modal');
  await dialog.getByRole('button', { name: /^Erase \d+ entr(y|ies)$/ }).click();
  // Erased, one line in the list's place says what went; the results and their actions are gone.
  await expect(dialog).toHaveCount(0);
  await expect(screen.locator('.actions')).toHaveCount(0, { timeout: 60_000 });
  expectTaskClicks(clicks, 'Privacy officer', 4);

  // 5. What retention will delete in the coming weeks.
  await start(page);
  clicks = new ClickCounter();
  await goToTab(page, 'Privacy', 'Retention', counted(clicks));
  await expect(page.locator('jfb-retention-screen uui-box').first()).toBeVisible({ timeout: 60_000 });
  expectTaskClicks(clicks, 'Privacy officer', 5);
});

test('a user without grants finds why no forms appear, how far setup is and whether anything needs attention without a click', async ({
  page,
  loginAs,
}) => {
  await loginAs('noAccess');
  const checklistRead = page.waitForResponse((response) => response.url().endsWith('/setup/checklist'));
  await start(page);
  const clicks = new ClickCounter();
  await expect(page.locator('jfb-no-forms')).toBeVisible({ timeout: 60_000 });
  expectTaskClicks(clicks, 'No access', 1);

  // How far the site's setup is: the checklist on the Overview while setup is open. Once it is complete, as other specs
  // may leave the shared host, the Overview's "..." shows it again, in the two clicks of the document's note.
  const complete = ((await (await checklistRead).json()) as { complete: boolean }).complete;
  const steps = page.locator('jfb-overview ol li');
  if (complete) {
    const again = new ClickCounter();
    await again.click(page.locator('jfb-overview umb-dropdown'));
    await again.click(page.locator('uui-menu-item[label="Show the setup checklist"]').filter({ visible: true }));
    await expect(steps).toHaveCount(5);
    expect(again.count, 'the checklist again, from the Overview\'s "..."').toBe(2);
  } else {
    await expect(steps).toHaveCount(5);
    expectTaskClicks(clicks, 'No access', 2);
  }
  await expect(page.locator('jfb-overview uui-box[aria-label="Health"]')).toBeVisible();
  expectTaskClicks(clicks, 'No access', 3);
});
