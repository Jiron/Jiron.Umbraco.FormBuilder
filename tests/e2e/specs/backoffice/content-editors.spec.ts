import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// The content and Set state editors (ROADMAP 10.2e, IA 5.3, ADR-0192): a content action is configured in its sidebar
// with the site's document type, where it goes (a page picked with Umbraco's picker), its name and a table of its
// properties, which starts with the property a field of its alias fills; its test is a dry run that says what a run
// would save, where, and which properties it sets, and the draft holds the settings as the server keeps them. Change
// the entry's state is tested the same way. The form check's Fix it of a property lands on its row.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

/** The demo site's root page, which an item may go under. */
const HOME = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    actions?: { settings?: Record<string, unknown> }[];
    pages: { groups: { fields: { key: string; alias: string }[] }[] }[];
    readonly [other: string]: unknown;
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** A field's key in a draft by its alias. */
const fieldKey = (draft: Draft, alias: string) =>
  draft.definition.pages
    .flatMap((page) => page.groups.flatMap((group) => group.fields))
    .find((field) => field.alias === alias)?.key;

/** Umbraco's open picker, over the sidebar. */
const modal = (page: Page) => page.locator('umb-backoffice-modal-container');

async function openActions(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  await expect(view.locator('.page')).toBeVisible();
  await expect(view.locator('.skeleton')).toHaveCount(0);
  return view;
}

/** Add action, and the choice of what happens. */
async function addAction(page: Page, view: Locator, choice: string) {
  await view.getByRole('button', { name: 'Add action' }).click();
  const sidebar = page.locator('jfb-action-modal');
  await sidebar.getByRole('radio', { name: choice, exact: true }).check();
  return sidebar;
}

/** Test from the sidebar, a dry run that asks nothing first, and the toast that says what a run would do. */
async function dryRun(page: Page, sidebar: Locator) {
  await sidebar.getByRole('button', { name: 'Test', exact: true }).click();
  const toast = page.locator('uui-toast-notification').filter({ hasText: 'Nothing was sent or changed.' });
  await expect(toast).toBeVisible();
  return toast;
}

test('a content action is configured with its document type, where it goes, its name and its properties, and its test says what a run would save', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Content editors'), [
    { type: 'text', alias: 'heading', label: { '': 'Headline' } },
    { type: 'textarea', alias: 'story', label: { '': 'Your story' } },
  ]);
  await loginAs('designer');
  const view = await openActions(page, key);
  const sidebar = await addAction(page, view, 'Create or update content');
  await sidebar.locator('#name').fill('Save each story');

  // The site's document types, which the designer has without Umbraco's Settings section; the table waits for one.
  await expect(sidebar.locator('#setting-mapping')).toHaveText('Choose a document type, and its properties come here.');
  await sidebar.getByRole('combobox', { name: 'Document type' }).selectOption({ label: 'Demo page' });
  // It starts with the property a field of its alias fills.
  await expect(sidebar.getByRole('combobox', { name: 'Property 1', exact: true })).toHaveValue('heading');
  await expect(sidebar.getByRole('combobox', { name: 'What Heading takes' }).locator('option:checked')).toHaveText(
    'The answer to Headline',
  );
  await sidebar.getByRole('button', { name: 'Add property' }).click();
  await expect(sidebar.getByRole('combobox', { name: 'Property 2', exact: true })).toBeFocused();
  await sidebar.getByRole('combobox', { name: 'Property 2', exact: true }).selectOption({ label: 'Introduction' });
  await sidebar
    .getByRole('combobox', { name: 'What Introduction takes' })
    .selectOption({ label: 'The answer to Your story' });
  await sidebar.getByRole('button', { name: 'Add property' }).click();
  await sidebar.getByRole('combobox', { name: 'Property 3', exact: true }).selectOption({ label: 'Form topic' });
  await sidebar.getByRole('combobox', { name: 'What Form topic takes' }).selectOption({ label: 'A value you write' });
  await expect(sidebar.getByRole('textbox', { name: 'Value of Form topic' })).toBeFocused();
  await page.keyboard.type('Stories');
  await page.keyboard.press('Tab');

  // The name before the place: Umbraco's picker gives the focus back to its button once its panel has gone, which
  // would take keys typed before then.
  await sidebar.getByRole('textbox', { name: 'Name of the item' }).fill('Story of {{ field.heading }}');

  // Where it goes: under the home page, picked with Umbraco's own picker.
  await expect(
    sidebar.getByRole('group', { name: 'Where it goes' }).getByRole('radio', { name: 'Under an item you pick' }),
  ).toBeChecked();
  const under = sidebar.getByRole('group', { name: 'Under this item' });
  await under.getByRole('button', { name: 'Choose' }).click();
  await modal(page).getByRole('button', { name: 'Home', exact: true }).click({ force: true });
  await modal(page).getByRole('button', { name: 'Choose', exact: true }).last().click();
  await expect(under.getByText('Home')).toBeVisible();
  await axeInside(page, 'jfb-action-modal');

  // A dry run: what a run would save, its name from the sample entry, where, and the properties it sets.
  const toast = await dryRun(page, sidebar);
  await expect(toast).toContainText(/A run would save an item of type Demo page named "Story of .+" under Home\./);
  await expect(toast).toContainText('It would set Heading, Introduction and Form topic.');

  await sidebar.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(view.locator('jfb-action-list .card .line').first()).toHaveText('Saves an item of type Demo page');
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect
    .poll(async () => (await draftOf(api, key)).definition.actions?.[0]?.settings, { timeout: 15_000 })
    .toBeDefined();
  const draft = await draftOf(api, key);
  expect(draft.definition.actions?.[0]?.settings).toEqual({
    documentType: 'demoPage',
    parent: HOME,
    name: 'Story of {{ field.heading }}',
    mapping: [
      { property: 'heading', field: fieldKey(draft, 'heading') },
      { property: 'lede', field: fieldKey(draft, 'story') },
      { property: 'formTopic', value: 'Stories' },
    ],
  });
});

test("Change the entry's state is configured and its test says the state a run would move the entry to", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('State editor'), [
    { type: 'text', alias: 'heading', label: { '': 'Headline' } },
  ]);
  await loginAs('designer');
  const view = await openActions(page, key);
  const sidebar = await addAction(page, view, "Change the entry's state");
  await expect(sidebar.getByText("Approving or rejecting starts that trigger's actions for the entry.")).toBeVisible();
  await sidebar.getByRole('combobox', { name: 'Move the entry to' }).selectOption({ label: 'Spam' });
  const toast = await dryRun(page, sidebar);
  await expect(toast).toContainText('A run would move the entry to Spam.');
  await sidebar.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(view.locator('jfb-action-list .card .line').first()).toHaveText('Moves the entry to Spam');
});

test("the form check's Fix it of a content action's property lands on its row, its problem under it", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Content check'), [
    { type: 'text', alias: 'heading', label: { '': 'Headline' } },
  ]);
  const draft = await draftOf(api, key);
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        actions: [
          {
            key: crypto.randomUUID(),
            type: 'content',
            name: 'Save each story',
            enabled: true,
            trigger: 'submitted',
            stopOnFailure: false,
            settings: {
              documentType: 'demoPage',
              name: 'Story',
              mapping: [
                { property: 'heading', field: fieldKey(draft, 'heading') },
                { property: 'subtitle', value: 'x' },
              ],
            },
          },
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveAccessibleName('Form check: 1 problem');
  await design.locator('jfb-form-canvas .bo-check').click();
  const check = page.locator('jfb-form-check-modal');
  await check.getByRole('button', { name: /^Fix it: .*Save each story/ }).click();
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('combobox', { name: 'Property 2', exact: true })).toBeFocused();
  // The check's own words for the row, which is named by its box.
  await expect(sidebar.locator('#setting-mapping-1--property-found-0')).toContainText(
    'The document type has no such property. Remove the row.',
  );
  await expect(sidebar.getByRole('combobox', { name: 'Property 2', exact: true }).locator('option:checked')).toHaveText(
    'subtitle (not on this document type)',
  );
});
