import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { fillForm } from '../../support/form.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { bodyOf, headerOf, visibleText, type Mail } from '../../support/mail.ts';
import { startStaticSite, type StaticSite } from '../../support/static-site.ts';

// A form in English and German, end to end (ROADMAP 14.1, ADR-0228): a designer switches the designer to German, finds
// what is left by its marks on the fields, the outcome's row and the action's card, writes the German texts there, and
// publishes; a visitor on a headless page then gets the form in each language, its error messages, its outcome and its
// email in that language.

test.describe.configure({ mode: 'serial' });

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;
const UNTRANSLATED = 'No German text yet';

let site: StaticSite | undefined;

test.beforeAll(async () => {
  site = await startStaticSite();
});

test.afterAll(async () => {
  await site?.stop();
});

/**
 * A headless form of a name and an address, with a mail to the address typed and an outcome, all written in English
 * only.
 */
async function englishForm(api: APIRequestContext): Promise<string> {
  const key = await formWithFields(api, unique('Languages'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' }, required: true },
    { type: 'email', alias: 'email', label: { '': 'Email address' }, required: true },
  ]);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { key: string; alias: string }[] }[] }[] } & Record<string, unknown>;
  };
  const email = draft.definition.pages[0]?.groups[0]?.fields.find((field) => field.alias === 'email');
  expect(email, 'the address field').toBeTruthy();
  draft.definition['actions'] = [
    {
      key: crypto.randomUUID(),
      type: 'email',
      name: 'Confirm to the visitor',
      enabled: true,
      trigger: 'submitted',
      stopOnFailure: false,
      settings: {
        toField: email?.key,
        subject: { '': 'We have your answer' },
        body: { '': '<p>Thank you, {{ field.name }}.</p>' },
      },
    },
  ];
  // Shown on the headless page as well as a Razor one.
  const settings = (draft.definition['settings'] ?? {}) as Record<string, Record<string, unknown>>;
  draft.definition['settings'] = { ...settings, developer: { ...settings['developer'], headless: true } };
  draft.definition['outcomes'] = [
    { key: crypto.randomUUID(), kind: 'message', message: { '': '<p>Thanks, it reached us.</p>' } },
  ];
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return key;
}

async function saveDraft(page: Page) {
  const saved = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok()).toBe(true);
}

/** Writes a rich text's German text in place of what the box shows. */
async function writeRich(box: Locator, text: string) {
  await box.click();
  await box.press('Control+A');
  await box.pressSequentially(text);
}

test('a form translated in the designer runs in English and German, its messages, outcome and email in each', async ({
  page,
  loginAs,
  apiAs,
  waitForMail,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await englishForm(api);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');

  // The Design view in German: each field is marked until its German label is written.
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const canvas = design.locator('jfb-form-canvas');
  await expect(design.locator('jfb-canvas-field').first()).toBeVisible();
  await canvas.getByRole('combobox', { name: 'Language' }).selectOption('de-DE');
  await expect(canvas.locator('jfb-canvas-field .bo-chip')).toHaveText([UNTRANSLATED, UNTRANSLATED]);
  const inspector = design.locator('jfb-designer-inspector');
  for (const [english, german] of [
    ['Your name', 'Ihr Name'],
    ['Email address', 'E-Mail-Adresse'],
  ] as const) {
    await design.getByRole('button', { name: new RegExp(`^${english}, `) }).click();
    await inspector.getByLabel('Label', { exact: true }).fill(german);
    await expect(design.getByRole('button', { name: new RegExp(`^${german}, `) })).toBeVisible();
  }
  await expect(canvas.locator('jfb-canvas-field .bo-chip')).toHaveCount(0);
  await saveDraft(page);

  // The Settings view, still in German: the outcome's row says what is left, and its sidebar takes the German message.
  // The views' tabs, not a new page: the language chosen stays with the workspace (ADR-0184).
  await page.getByRole('tab', { name: 'Settings', exact: true }).click();
  const settings = page.locator('jfb-form-settings-view');
  const row = settings.locator('jfb-outcome-list .outcome-row').first();
  await expect(row.locator('.untranslated')).toHaveText(UNTRANSLATED);
  await row.locator('.row-open').click();
  const outcome = page.locator('jfb-outcome-modal');
  await writeRich(outcome.locator('#message [contenteditable="true"]'), 'Danke, es ist bei uns angekommen.');
  await outcome.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(outcome).toHaveCount(0);
  await expect(row.locator('.untranslated')).toHaveCount(0);

  // The Actions view: the mail's card is marked until its subject and body are German too.
  await page.getByRole('tab', { name: 'Actions', exact: true }).click();
  const actions = page.locator('jfb-form-actions-view');
  const card = actions.locator('jfb-action-list').locator('.open').first();
  await expect(card.locator('.untranslated')).toHaveText(UNTRANSLATED);
  await card.click();
  const sidebar = page.locator('jfb-action-modal');
  const subject = sidebar.getByRole('textbox', { name: 'Subject' });
  await expect(subject).toHaveAttribute('placeholder', 'We have your answer');
  await subject.fill('Wir haben Ihre Antwort');
  await writeRich(sidebar.locator('#setting-body [contenteditable="true"]'), 'Danke, ');
  await sidebar.getByRole('button', { name: 'Insert merge tag' }).last().click();
  await sidebar.getByRole('button', { name: /\{\{ field\.name \}\}/ }).click();
  await sidebar.locator('[slot="actions"]').getByRole('button', { name: 'Update', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(card.locator('.untranslated')).toHaveCount(0);

  // Published once saved.
  await saveDraft(page);
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  await page.locator('jfb-publish-modal').getByRole('button', { name: 'Publish', exact: true }).click();
  await expect.poll(async () => (await formOf(api, key)).isPublished).toBe(true);

  // A visitor in each language: the labels, a message, the outcome and the mail in it.
  if (site === undefined) throw new Error('The static site did not start.');
  for (const language of [
    {
      culture: 'de-DE',
      labels: ['Ihr Name', 'E-Mail-Adresse'],
      invalid: 'Geben Sie eine vollst',
      outcome: 'Danke, es ist bei uns angekommen.',
      subject: 'Wir haben Ihre Antwort',
      greeting: 'Danke, Ada',
    },
    {
      culture: 'en-US',
      labels: ['Your name', 'Email address'],
      invalid: 'Enter a complete email address',
      outcome: 'Thanks, it reached us.',
      subject: 'We have your answer',
      greeting: 'Thank you, Ada.',
    },
  ]) {
    await page.goto(`${site.url}/?${new URLSearchParams({ form: key, culture: language.culture })}`);
    const form = page.locator('jfb-form form.jfb-form');
    await expect(form).toHaveAttribute('lang', language.culture);
    await expect(form.locator('label.jfb-label')).toContainText(language.labels);
    await form.getByLabel(language.labels[1] ?? '').fill('not-an-address');
    await form.getByLabel(language.labels[1] ?? '').press('Tab');
    await expect(form.locator('[data-jfb-error]').filter({ hasText: language.invalid })).toHaveCount(1);

    const address = `languages-${language.culture}-${Date.now()}@example.test`;
    await fillForm(form, { name: 'Ada', email: address });
    await form.locator('button[data-jfb-nav="submit"]').click();
    await expect(page.locator('jfb-form')).toContainText(language.outcome);

    const mail: Mail = await waitForMail((each) => headerOf(each.raw, 'To').includes(address), 60_000);
    expect(headerOf(mail.raw, 'Subject')).toBe(language.subject);
    const html = bodyOf(mail.raw, 'text/html');
    expect(html).toContain(`lang="${language.culture.slice(0, 2)}`);
    expect(visibleText(html)).toContain(language.greeting);
  }
});
