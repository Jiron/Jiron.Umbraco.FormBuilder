import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { chooseLanguage } from '../../support/designer-row.ts';

// The AI add-on in the designer (ROADMAP 15.3, ADR-0018), on the fake provider whose chat profile this host chooses in
// Settings > AI (Testing.AI; no real key, ever): "Draft a form with AI" in the library's create menu turns a pasted
// questionnaire into a new form in the designer, marked "Drafted by AI, please review" until its first save; "Translate
// with AI" in the canvas's "..." previews the German texts beside their sources, takes the editor's changes and applies
// what stays ticked; "Suggest help text" beside a field's help text lists what the AI suggests and writes the one used
// into the draft; and Settings > AI names the profile the features use.

const SECTION = '/umbraco/section/jfb-forms';

interface Field {
  readonly alias?: string;
  readonly label?: Record<string, string>;
  readonly help?: Record<string, string>;
}

/** The fields of a form's saved draft, by alias. */
async function fieldsOf(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: Field[] }[] }[] };
  };
  return new Map(
    draft.definition.pages
      .flatMap((page) => page.groups.flatMap((group) => group.fields))
      .map((field) => [field.alias, field]),
  );
}

async function openDesigner(page: Page, key: string) {
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field').first()).toBeVisible();
  return design;
}

async function saveDraft(page: Page) {
  const saved = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok()).toBe(true);
}

test('Draft a form with AI turns a pasted questionnaire into a new form, marked for review until its first save', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);
  await expect(page.locator('umb-section-sidebar')).toBeVisible();

  // The last way to a new form in the library's create menu.
  await page.locator('umb-section-sidebar').getByRole('button', { name: 'Create' }).first().click();
  const options = page.locator('umb-entity-create-option-action-list-modal umb-ref-item');
  await expect(options.last()).toHaveAttribute('name', 'Draft a form with AI\u2026');
  await options.last().click();

  const sidebar = page.locator('jfb-ai-draft-modal');
  const text = sidebar.getByRole('textbox', { name: 'Describe the form, or paste the text of a questionnaire' });
  await expect(text).toBeFocused();
  await expect(sidebar.getByRole('button', { name: 'Draft form' })).toBeDisabled();
  await text.fill(
    [
      'Summer reading club sign-up',
      'Which age group are you in? (Children / Teens / Adults)',
      'Which books did you enjoy most this year?',
    ].join('\n'),
  );
  await axeInside(page, 'jfb-ai-draft-modal');
  await sidebar.getByRole('button', { name: 'Draft form' }).click();

  // The new form opens in the designer with what the AI drafted, the questions as fields of the fitting types.
  await expect(page).toHaveURL(/\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/);
  const key = page.url().split('/').at(-1) ?? '';
  const design = page.locator('jfb-form-design-view');
  const fields = design.locator('jfb-canvas-field[key]');
  await expect(fields).toHaveCount(4);
  await expect(fields.nth(0)).toContainText('Name');
  await expect(fields.nth(1)).toContainText('Email address');
  await expect(fields.nth(2)).toContainText('Which age group are you in?');
  await expect(fields.nth(2)).toContainText('Teens');
  await expect(fields.nth(3)).toContainText('Which books did you enjoy most this year?');
  const { name } = (await (await api.get(`${API}/form/${key}`)).json()) as { name: string };
  expect(name).toBe('Summer reading club sign-up');

  // The mark in the footer says it is a draft to review; the first save takes it away.
  const mark = page.locator('jfb-ai-drafted-mark').getByText('Drafted by AI, please review');
  await expect(mark).toBeVisible();
  await design.getByRole('button', { name: /^Name, / }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill('Your name');
  await saveDraft(page);
  await expect(mark).toBeHidden();
});

test('Translate with AI previews the German texts beside their sources and applies what stays ticked', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Library visit'), [
    { type: 'text', alias: 'visitor', label: { '': 'Your name' }, help: { '': 'As on your library card' } },
    { type: 'email', alias: 'email', label: { '': 'Email address' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await openDesigner(page, key);

  await design.getByRole('button', { name: 'More actions for the canvas' }).click();
  await design.getByRole('button', { name: 'Translate with AI\u2026' }).click();
  const sidebar = page.locator('jfb-ai-translate-modal');
  const language = sidebar.getByRole('combobox', { name: 'Language' });
  await expect(language).toBeFocused();
  await expect(language).toHaveValue('de-DE');
  await sidebar.getByRole('button', { name: 'Translate', exact: true }).click();

  // Each text beside its translation, which the editor may change or untick.
  const first = sidebar.getByRole('textbox', { name: 'Translation 1: Your name' });
  await expect(first).toHaveValue('Your name (de)');
  await expect(sidebar.getByRole('textbox', { name: 'Translation 2: As on your library card' })).toHaveValue(
    'As on your library card (de)',
  );
  await first.fill('Ihr Name');
  await sidebar.getByRole('checkbox', { name: 'Use the translation of \u201cEmail address\u201d' }).uncheck();
  await axeInside(page, 'jfb-ai-translate-modal');
  await sidebar.getByRole('button', { name: 'Apply' }).click();
  await expect(sidebar).toBeHidden();

  // Applied to the open draft, saved when the editor saves; the unticked text keeps no German.
  await saveDraft(page);
  const saved = await fieldsOf(api, key);
  expect(saved.get('visitor')?.label).toEqual({ '': 'Your name', 'de-DE': 'Ihr Name' });
  expect(saved.get('visitor')?.help).toEqual({
    '': 'As on your library card',
    'de-DE': 'As on your library card (de)',
  });
  expect(saved.get('email')?.label).toEqual({ '': 'Email address' });
});

test('Suggest help text lists what the AI suggests beside the help text, and writes the one used into the draft', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Membership'), [{ type: 'text', alias: 'dob', label: { '': 'DOB' } }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await openDesigner(page, key);
  await design.getByRole('button', { name: /^DOB, / }).click();
  const inspector = design.locator('jfb-designer-inspector');

  await inspector.getByRole('button', { name: 'Suggest help text' }).click();
  const panel = inspector.getByRole('group', { name: 'Suggestions' });
  const items = panel.getByRole('listitem');
  await expect(items).toHaveCount(2);
  await expect(items.nth(0)).toContainText('Fake help for DOB.');
  await expect(items.nth(1)).toContainText('DOB written out');
  await expect(items.nth(1)).toContainText('An abbreviation is hard to understand.');
  await axeInside(page, 'jfb-designer-inspector');

  await panel.getByRole('button', { name: 'Use as the help text: Fake help for DOB.' }).click();
  await expect(items).toHaveCount(1);
  await expect(inspector.getByLabel('Help text', { exact: true })).toHaveValue('Fake help for DOB.');

  // Not offered while the designer shows another language: suggestions start in the default one.
  await chooseLanguage(design, 'de-DE');
  await expect(inspector.getByRole('button', { name: 'Suggest help text' })).toHaveCount(0);

  await saveDraft(page);
  expect((await fieldsOf(api, key)).get('dob')?.help).toEqual({ '': 'Fake help for DOB.' });
});

test('Settings > AI shows the profile the AI features use, once', async ({ page, loginAs }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(SECTION);
  await page.locator('umb-section-sidebar').getByRole('link', { name: 'AI', exact: true }).click();

  const screen = page.locator('jfb-ai-settings-screen');
  await expect(screen.getByRole('heading', { name: 'AI profile' })).toBeVisible();
  await expect(screen.locator('uai-profile-picker')).toBeVisible();
  // The picker shows the chosen profile, which nothing on the screen says again.
  await expect(screen.getByText('Fake chat (tests and demo)', { exact: true })).toBeVisible();
  await expect(screen.locator('.status')).toBeEmpty();
  await axeInside(page, 'jfb-ai-settings-screen');
});
