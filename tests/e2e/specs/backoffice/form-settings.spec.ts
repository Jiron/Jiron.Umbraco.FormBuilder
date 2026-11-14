import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';

// The form's Settings view (ROADMAP 10.1a, IA 5.2, ADR-0184). Submission is open and the other groups are closed, each
// with the line of what is set; each setting changes the draft, which Save draft keeps, and a value the view can't keep
// is said under its box once the box is left. The form check's problems in the settings show under their setting, and
// its Fix it, from the Design view's check and from the publish dialog, opens the group with the focus on the setting.

const SECTION = '/umbraco/section/jfb-forms';
const ELLIPSIS = String.fromCharCode(0x2026);
/** The dot between the parts of a closed group's line. */
const DOT = String.fromCharCode(0xb7);

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    readonly pages: { readonly groups: { readonly fields: { readonly key: string; readonly alias?: string }[] }[] }[];
    settings: Record<string, Record<string, unknown>>;
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** A form with a name and an email field, its key and the fields' keys by alias. */
async function newForm(api: APIRequestContext, name: string) {
  const key = await formWithFields(api, unique(name), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  const fields = Object.fromEntries(
    (await draftOf(api, key)).definition.pages[0]!.groups[0]!.fields.map((field) => [field.alias, field.key]),
  );
  return { key, fields };
}

/** The site's choices the view reads for a form: its time zone among them. */
const choicesOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/setting-choices`)).json()) as { timeZone: string };

async function openSettings(page: Page, key: string, width = 1440) {
  await page.setViewportSize({ width, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/settings`);
  const view = page.locator('jfb-form-settings-view');
  await expect(view.locator('#group-submission')).toBeVisible();
  return view;
}

const headOf = (view: Locator, group: string) => view.locator(`#group-${group}`);

async function openGroup(view: Locator, group: string) {
  const head = headOf(view, group);
  if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
  await expect(head).toHaveAttribute('aria-expanded', 'true');
}

/** Types into a box and leaves it, as a person does: the view says a value it can't keep once the box is left. */
async function enter(box: Locator, value: string) {
  await box.fill(value);
  await box.press('Tab');
}

/** Saves the draft and waits for the save's own answer, which the button does not wait for. */
async function saveDraft(page: Page, key: string) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
}

/** An instant on a zone's clock, as the definition stores it: the test finds it by asking the browser's own data. */
async function instantOn(page: Page, local: string, zone: string): Promise<string> {
  return page.evaluate(
    ([wall, timeZone]) => {
      const parts = (instant: number) =>
        Object.fromEntries(
          new Intl.DateTimeFormat('en-US', {
            timeZone,
            hourCycle: 'h23',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
          })
            .formatToParts(new Date(instant))
            .map((part) => [part.type, part.value]),
        );
      const asUtc = Date.parse(`${wall}:00Z`);
      for (let offset = -14 * 60; offset <= 14 * 60; offset += 15) {
        const instant = asUtc - offset * 60_000;
        const shown = parts(instant);
        if (`${shown['year']}-${shown['month']}-${shown['day']}T${shown['hour']}:${shown['minute']}` === wall)
          return new Date(instant).toISOString().replace(/\.\d{3}Z$/, 'Z');
      }
      throw new Error(`no instant shows ${wall} in ${timeZone}`);
    },
    [local, zone] as const,
  );
}

test('the submission group and its More options set what they say, refuse what they cannot keep, and Save draft keeps them', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key, fields } = await newForm(api, 'Settings');
  await loginAs('designer');
  const view = await openSettings(page, key);

  // Submission open, the others closed, each with its line.
  await expect(headOf(view, 'submission')).toHaveAttribute('aria-expanded', 'true');
  for (const group of ['availability', 'protection', 'appearance', 'developer'])
    await expect(headOf(view, group)).toHaveAttribute('aria-expanded', 'false');
  await expect(headOf(view, 'availability').locator('.summary')).toHaveText(`Always open ${DOT} No limit`);
  await expect(headOf(view, 'developer').locator('.summary')).toHaveText('Headless access off');
  await axeInside(page, 'jfb-form-settings-view');

  await view.getByLabel('Send button').fill('Send request');
  await view.getByLabel('Store entries').selectOption('never');
  await view.getByRole('switch', { name: 'Manual approval' }).check();

  // Verification confirms the email field, with the link's days in bounds.
  await view.getByRole('switch', { name: 'Email verification' }).check();
  await expect(view.getByLabel('Address to confirm')).toHaveValue(fields['email']!);
  const days = view.getByLabel('Link valid for (days)');
  await enter(days, '91');
  await expect(view.locator('#verification-days-why-0')).toHaveText('Enter a whole number from 1 to 90.');
  await expect(days).toHaveAttribute('aria-invalid', 'true');
  await enter(days, '14');
  await expect(view.locator('#verification-days-why-0')).toHaveCount(0);

  // The entry title takes a merge tag from its picker, where the caret is.
  const title = view.getByLabel('Entry title');
  await title.fill('Request from ');
  await view.getByRole('button', { name: 'Insert merge tag' }).click();
  await view.getByRole('button', { name: 'Name {{ field.name }}' }).click();
  await expect(title).toHaveValue('Request from {{ field.name }}');
  await expect(title).toBeFocused();
  // More options: the buttons between pages, the summary page, the progress, saving for later (its own spec) and the reference.
  // More options: the buttons between pages, the summary page, the progress and the reference with its example.
  const more = view.getByRole('button', { name: 'More options (6)' });
  await more.click();
  await expect(view.getByRole('button', { name: 'Fewer options' })).toHaveAttribute('aria-expanded', 'true');
  await expect(view.getByLabel('Next page')).toHaveAttribute('placeholder', 'Next');
  await view.getByLabel('Next page').fill('Continue');
  await view.getByLabel('Previous page').fill('Go back');
  await view.getByRole('switch', { name: 'Summary page' }).check();
  await view.getByLabel('Progress', { exact: true }).selectOption('bar');
  await view.getByLabel('Prefix').fill('REQ-');
  await expect(view.locator('.example code')).toHaveText(`REQ-${new Date().getFullYear()}-00042`);
  const digits = view.getByLabel('Digits');
  await enter(digits, '11');
  await expect(view.locator('#reference-digits-why-0')).toHaveText('Enter a whole number from 1 to 10.');
  await enter(digits, '3');
  await view.getByRole('switch', { name: 'Include the year' }).uncheck();
  await expect(view.locator('.example code')).toHaveText('REQ-042');
  await axeInside(page, 'jfb-form-settings-view');

  // Ctrl+Z outside a text box takes the last change back, and says so.
  await view.getByRole('switch', { name: 'Include the year' }).press('Control+z');
  await expect(view.locator('.example code')).toHaveText(`REQ-${new Date().getFullYear()}-042`);
  await expect(view.locator('#said')).toHaveText('Undone: Changed Entry reference');

  await saveDraft(page, key);
  const submission = (await draftOf(api, key)).definition.settings['submission'];
  expect(submission).toMatchObject({
    submitLabel: { '': 'Send request' },
    nextLabel: { '': 'Continue' },
    backLabel: { '': 'Go back' },
    summaryPage: true,
    progress: 'bar',
    store: 'never',
    approval: 'manual',
    verification: { emailField: fields['email'], days: 14 },
    entryTitle: 'Request from {{ field.name }}',
    entryReference: { prefix: 'REQ-', year: true, digits: 3 },
  });

  // A closed group says what is set.
  await headOf(view, 'submission').click();
  await expect(headOf(view, 'submission').locator('.summary')).toHaveText(
    `Entries not stored ${DOT} Manual approval ${DOT} Email verification`,
  );
});

test('retention: a number of days to store entries for and the days per state each say their range, and Save draft keeps them', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key } = await newForm(api, 'Retention days');
  await loginAs('designer');
  const view = await openSettings(page, key);

  // Store entries for a number of days: 30 at first, and a number out of range says why.
  await view.getByLabel('Store entries').selectOption('days');
  const storeDays = view.getByLabel('Days entries are kept');
  await expect(storeDays).toHaveValue('30');
  await enter(storeDays, '3651');
  await expect(view.locator('#store-days-why-0')).toHaveText('Enter a whole number from 1 to 3,650.');
  await expect(storeDays).toHaveAttribute('aria-invalid', 'true');
  await enter(storeDays, '400');
  await expect(view.locator('#store-days-why-0')).toHaveCount(0);

  // Privacy's days per state: each empty box shows the site's days, and a number out of range says why.
  await openGroup(view, 'privacy');
  const spam = view.locator('#retention-spam');
  await expect(spam).toHaveAttribute('placeholder', '30');
  await expect(view.locator('#retention-submitted')).toHaveAttribute('placeholder', '365');
  await enter(spam, '0');
  await expect(view.locator('#retention-spam-why-0')).toHaveText('Enter a whole number from 1 to 3,650.');
  await enter(spam, '7');
  await expect(view.locator('#retention-spam-why-0')).toHaveCount(0);
  const rejected = view.locator('#retention-rejected');
  await enter(rejected, '40000');
  await expect(view.locator('#retention-rejected-why-0')).toHaveText('Enter a whole number from 1 to 3,650.');
  await enter(rejected, '14');
  await axeInside(page, 'jfb-form-settings-view');

  await saveDraft(page, key);
  const settings = (await draftOf(api, key)).definition.settings;
  expect(settings['submission']).toMatchObject({ store: 'days', storeDays: 400 });
  expect(settings['privacy']).toMatchObject({ retentionDays: { spam: 7, rejected: 14 } });

  // A closed group says what is set.
  await headOf(view, 'privacy').click();
  await expect(headOf(view, 'privacy').locator('.summary')).toHaveText(`Own retention days ${DOT} Insights on`);
  await headOf(view, 'submission').click();
  await expect(headOf(view, 'submission').locator('.summary')).toHaveText('Entries kept 400 days');
});

test("IP addresses: the Privacy group chooses what the form's entries keep, and an entry it then takes keeps the address that way", async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const { key } = await newForm(api, 'IP addresses');
  await loginAs('admin');
  const view = await openSettings(page, key);
  await openGroup(view, 'privacy');
  const ip = view.getByLabel('IP addresses', { exact: true });
  await expect(ip.locator('option')).toHaveText([
    'Site default (No IP addresses)',
    'Keep none',
    'Anonymised, the end removed',
    'Full addresses',
  ]);
  // The host doesn't allow full addresses, and the help names the key that would.
  await expect(ip.locator('option[value="full"]')).toBeDisabled();
  await expect(view.locator('#ip-help')).toHaveText(
    "Full addresses need JironFormBuilder:Privacy:AllowFullIp in the site's configuration.",
  );
  await ip.selectOption('anonymised');
  await axeInside(page, 'jfb-form-settings-view');
  await saveDraft(page, key);
  expect((await draftOf(api, key)).definition.settings['privacy']).toMatchObject({ ip: 'anonymised' });
  await headOf(view, 'privacy').click();
  await expect(headOf(view, 'privacy').locator('.summary')).toHaveText(`Anonymised IP addresses ${DOT} Insights on`);

  // Published for headless sites, it takes an entry from this machine, and keeps its address with the end removed.
  const draft = await draftOf(api, key);
  const developer = { ...(draft.definition.settings['developer'] ?? {}), headless: true };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: { ...draft.definition, settings: { ...draft.definition.settings, developer } },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  let revision = 0;
  await expect
    .poll(async () => {
      const served = await request.get(`/jfb/api/v1/forms/${key}`);
      revision = served.ok() ? ((await served.json()) as { revision: number }).revision : 0;
      return revision;
    })
    .toBeGreaterThan(0);
  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision, challenge, values: { name: 'Ada Lovelace', email: 'ada@example.com' } },
  });
  expect(sent.ok(), await sent.text()).toBe(true);

  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/entries`);
  const entries = page.locator('jfb-entries-view');
  await entries.locator('tbody tr').first().locator('.name').click();
  const panel = entries.locator('jfb-entry-panel');
  await panel.getByRole('button', { name: 'More details' }).click();
  const address = panel.locator('dl.more dt', { hasText: 'IP address' }).locator('xpath=following-sibling::dd[1]');
  await expect(address).toHaveText('127.0.0.0');
});

test('availability, spam protection, appearance and developer set what they say, refuse what they cannot keep, and Save draft keeps them', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key } = await newForm(api, 'Settings');
  const { timeZone } = await choicesOf(api, key);
  await loginAs('designer');
  const view = await openSettings(page, key);

  // Availability: the times in the time zone of all sites, a closing before the opening refused, the limit, the message,
  // members.
  await openGroup(view, 'availability');
  await expect(view.locator('#body-availability .note').first()).toHaveText(
    `Times are in the time zone set for all sites: ${timeZone}.`,
  );
  await enter(view.getByLabel('Opens'), '2026-12-01T09:00');
  await enter(view.getByLabel('Closes'), '2026-11-30T18:00');
  await expect(view.locator('#closes-at-why-0')).toHaveText('This is before it opens. Choose a later time.');
  await enter(view.getByLabel('Closes'), '2026-12-24T18:00');
  await expect(view.locator('#closes-at-why-0')).toHaveCount(0);
  const limit = view.getByLabel('Entry limit');
  await enter(limit, 'many');
  await expect(view.locator('#max-entries-why-0')).toContainText('Enter a whole number from 1 to');
  await enter(limit, '120');
  const message = view.locator('#closed-message [contenteditable="true"]');
  await message.click();
  await message.pressSequentially('Registration has closed.');
  await view.getByLabel('Who can fill it in').selectOption('groups');
  await view.locator('#member-groups').getByLabel('Library members').check();
  await axeInside(page, 'jfb-form-settings-view');

  // Spam protection: the site's choice named, providers without keys dimmed, a provider's own setting in bounds.
  await openGroup(view, 'protection');
  const choices = view.locator('.choice');
  await expect(choices.first().locator('.choice-name')).toHaveText(/^Site default \(.+\)$/);
  const scored = choices.filter({ hasText: 'reCAPTCHA v3' });
  await expect(scored).toHaveAttribute('data-dimmed', '');
  await expect(scored.locator('.choice-line')).toContainText('no keys for it yet');
  await scored.locator('input').check();
  const threshold = view.getByLabel('Lowest score');
  await enter(threshold, '2');
  await expect(view.locator('#setting-captcha-threshold-why')).toHaveText('Enter 1 or less.');
  await enter(threshold, '0.7');
  await expect(view.locator('#setting-captcha-threshold-why')).toHaveCount(0);
  await view.getByLabel('Content checks', { exact: true }).selectOption('strict');

  // Appearance: the site's theme and marks named, the form's own chosen.
  await openGroup(view, 'appearance');
  await expect(view.getByLabel('Theme').locator('option').first()).toHaveText(/^\s*Site default \(.+\)\s*$/);
  await view.getByLabel('Theme').selectOption('unstyled');
  await view.getByLabel('Fields that get a mark').selectOption('required');
  await view.getByRole('switch', { name: 'List of problems' }).uncheck();
  await view.getByLabel('Choices').selectOption('cards');
  const css = view.getByLabel('CSS class');
  await enter(css, 'x'.repeat(501));
  await expect(view.locator('#css-class-why-0')).toHaveText('Use 500 characters at most.');
  await enter(css, 'wide');

  // Developer: headless access, and the names one a line as the server checks them.
  await openGroup(view, 'developer');
  await view.getByRole('switch', { name: 'Headless access' }).check();
  const query = view.getByLabel('Query values');
  await enter(query, 'ref\nwith space');
  await expect(view.locator('#query-values-why-0')).toHaveText(
    '"with space" isn\'t a name. Use up to 64 letters, digits, hyphens and underscores.',
  );
  await enter(query, 'ref\nutm_source');
  const context = view.getByLabel('Context values');
  await enter(context, 'jfbServer');
  await expect(view.locator('#context-values-why-0')).toHaveText(
    '"jfbServer" starts with "jfb", which Jiron Form Builder keeps for itself.',
  );
  await enter(context, 'crm');
  await axeInside(page, 'jfb-form-settings-view');

  await saveDraft(page, key);
  const settings = (await draftOf(api, key)).definition.settings;
  expect(settings['availability']).toMatchObject({
    opensAt: await instantOn(page, '2026-12-01T09:00', timeZone),
    closesAt: await instantOn(page, '2026-12-24T18:00', timeZone),
    maxEntries: 120,
    members: 'groups',
  });
  expect((settings['availability']?.['closedMessage'] as Record<string, string>)['']).toContain(
    'Registration has closed.',
  );
  expect(settings['availability']?.['memberGroups']).toHaveLength(1);
  expect(settings['protection']).toEqual({
    level: 'captcha',
    captcha: { provider: 'recaptchaV3', settings: { threshold: '0.7' } },
    contentRules: 'strict',
  });
  expect(settings['appearance']).toMatchObject({
    theme: 'unstyled',
    requiredMarker: 'required',
    validationSummary: false,
    choiceLayout: 'cards',
    cssClass: 'wide',
  });
  expect(settings['developer']).toEqual({ headless: true, queryValues: ['ref', 'utm_source'], contextValues: ['crm'] });

  // Each closed group says what is set.
  for (const group of ['availability', 'protection', 'developer']) await headOf(view, group).click();
  await expect(headOf(view, 'availability').locator('.summary')).toContainText(
    `120 entries at most ${DOT} Members of 1 group`,
  );
  await expect(headOf(view, 'protection').locator('.summary')).toHaveText(`reCAPTCHA v3 ${DOT} Strict content checks`);
  await expect(headOf(view, 'developer').locator('.summary')).toHaveText(
    `Headless access on ${DOT} 2 query values ${DOT} 1 context value`,
  );
});

test("the form check's problems in the settings show under their setting, and Fix it leads to it from the check and from Publish", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key, fields } = await newForm(api, 'Settings check');
  // A form for some member groups that names none, and verification of a field that is no email field: two errors.
  const draft = await draftOf(api, key);
  const settings = draft.definition.settings;
  settings['availability'] = { ...settings['availability'], members: 'groups' };
  settings['submission'] = { ...settings['submission'], verification: { emailField: fields['name'], days: 7 } };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await loginAs('designer');

  // From the Design view's check: Fix it on the member groups opens Availability on them, the problem said there.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveText('2 problems');
  await design.locator('jfb-form-canvas .bo-check').click();
  const sidebar = page.locator('jfb-form-check-modal');
  await sidebar.locator('li[data-code="part_missing"]').getByRole('button', { name: 'Fix it: Form settings' }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(page).toHaveURL(/\/view\/settings$/);
  const view = page.locator('jfb-form-settings-view');
  await expect(headOf(view, 'availability')).toHaveAttribute('aria-expanded', 'true');
  await expect(view.locator('#member-groups').getByLabel('Library members')).toBeFocused();
  await expect(view.locator('#member-groups-why-0')).toHaveText('The form is for some member groups, but names none.');
  await axeInside(page, 'jfb-form-settings-view');

  // A closed group counts its problems on its line.
  await headOf(view, 'submission').click();
  await expect(headOf(view, 'submission').locator('.flag')).toHaveText('1 problem');

  // From the publish dialog, while the view is shown: Fix it on the verification's field opens Submission on it.
  await page.getByRole('button', { name: `Publish${ELLIPSIS}` }).click();
  const dialog = page.locator('jfb-publish-modal');
  await expect(dialog.locator('[role="alert"] .lead')).toHaveText(
    "This form can't be published yet: 2 errors need fixing.",
  );
  await dialog
    .locator('li[data-code="invalid_reference"]')
    .getByRole('button', { name: 'Fix it: Form settings' })
    .click();
  await expect(dialog).toHaveCount(0);
  await expect(headOf(view, 'submission')).toHaveAttribute('aria-expanded', 'true');
  await expect(view.getByLabel('Address to confirm')).toBeFocused();
  await expect(view.locator('#verification-field-why-0')).toHaveText(
    "It refers to a field or an option that doesn't fit here.",
  );

  // Fixed here, the problems go once the check has looked again.
  await view.getByLabel('Address to confirm').selectOption(fields['email']!);
  await view.locator('#member-groups').getByLabel('Library members').check();
  await expect(view.locator('[id$="-why-0"]')).toHaveCount(0);
});
