import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, contactForm, formOf, publishWithLabel, unique } from '../../support/forms-api.ts';

// The form workspace (ROADMAP 9.3a, ADR-0166, docs/ux/information-architecture.md section 5): it loads a form's draft, renames
// the form in its header and saves through the API, publishes after asking, asks before leaving unsaved changes, opens a
// field's search result with that field selected, and shows each user the views and actions their permissions allow.
// Its Design view (ROADMAP 9.3b, ADR-0168): a form of three pages built with the mouse, from the palette and an insert
// point; five fields from a pasted list; and, in the narrowest workspace, the palette over the canvas, the skip link,
// and a delete that Undo brings back.

const SECTION = '/umbraco/section/jfb-forms';

const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

/** A form's row in the forms tree, by the name its label carries (a row may hold more text for screen readers). */
const treeRow = (page: Page, name: string) =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name}"]`).filter({ visible: true });

/** The form workspace's name field. */
const nameField = (page: Page) => page.locator('jfb-form-workspace-editor').getByRole('textbox', { name: 'Form name' });

interface DraftField {
  readonly type: string;
  readonly label?: Record<string, string>;
}

/** A draft's fields, page by page, read through the API; a draft leaves its empty lists out. */
async function draftFields(api: APIRequestContext, key: string): Promise<DraftField[][]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages?: { groups?: { fields?: DraftField[] }[] }[] };
  };
  return (draft.definition.pages ?? []).map((each) => (each.groups ?? []).flatMap((group) => group.fields ?? []));
}

/** The types of a draft's fields, page by page. */
const draftTypes = async (api: APIRequestContext, key: string) =>
  (await draftFields(api, key)).map((fields) => fields.map((field) => field.type));

/** The labels of a draft's fields, in order, in the default language. */
const draftLabels = async (api: APIRequestContext, key: string) =>
  (await draftFields(api, key)).flat().map((field) => field.label?.[''] ?? '');

test('the header renames a form, Save draft saves it, and Publish\u2026 publishes it after asking', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const name = unique('Workshop');
  const renamed = `${name} 2027`;
  const key = await contactForm(api, name);
  await loginAs('designer');
  await page.goto(workspaceOf(key));

  await expect(nameField(page)).toHaveValue(name);
  const editor = page.locator('jfb-form-workspace-editor');
  await expect(editor.locator('uui-tag')).toHaveText('Draft');
  // The designer sees the views a designer uses, and the form opens on Design with its fields.
  for (const view of ['Design', 'Settings', 'Actions', 'History'])
    await expect(page.getByRole('tab', { name: view })).toBeVisible();
  for (const view of ['Entries', 'Insights']) await expect(page.getByRole('tab', { name: view })).toHaveCount(0);
  const design = page.locator('jfb-form-design-view');
  await expect(design.getByRole('button', { name: /^Email address/ })).toBeVisible();
  await axeInside(page, 'jfb-form-workspace-editor');

  await nameField(page).fill(renamed);
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect.poll(async () => (await formOf(api, key)).name).toBe(renamed);
  await expect(treeRow(page, renamed)).toBeVisible();

  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  const dialog = page.locator('jfb-publish-modal');
  await expect(dialog).toContainText(`Publish ${renamed}?`);
  await axeInside(page, 'jfb-publish-modal');
  await dialog.getByRole('textbox', { name: 'Comment for this version (optional)' }).fill('First version');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(editor.locator('uui-tag')).toHaveText('Published');
  await expect.poll(async () => (await formOf(api, key)).isPublished).toBe(true);
  const versions = (await (await api.get(`${API}/form/${key}/versions`)).json()) as {
    items: { version: { comment?: string | null; isLive: boolean } }[];
  };
  expect(versions.items.find((entry) => entry.version.isLive)?.version.comment).toBe('First version');
});

test('leaving a renamed form unsaved asks first, and Stay keeps the change', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const name = unique('Unsaved');
  const key = await contactForm(api, name);
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  await expect(nameField(page)).toHaveValue(name);

  await nameField(page).fill(`${name} changed`);
  // Another form in the tree: the demo's contact form.
  await page.locator('umb-section-sidebar uui-menu-item[label="Contact"]').filter({ visible: true }).first().click();
  const discard = page.locator('umb-discard-changes-modal, uui-dialog-layout').filter({ hasText: 'Discard' }).first();
  await expect(discard).toBeVisible();
  await discard.getByRole('button', { name: 'Stay' }).click();
  await expect(nameField(page)).toHaveValue(`${name} changed`);
  expect((await formOf(api, key)).name).toBe(name);
});

test('a blank form opens on Design with its empty group, though its draft leaves the empty lists out', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const name = unique('Blank');
  const key = await blankForm(await apiAs('designer'), name);
  await loginAs('designer');
  await page.goto(workspaceOf(key));

  await expect(nameField(page)).toHaveValue(name);
  await expect(page.locator('jfb-form-design-view')).toContainText(
    'Add your first field: pick a type on the left, or paste your questions.',
  );
  await axeInside(page, 'jfb-form-design-view');
});

test('a form of three pages is built with the mouse, from the palette and an insert point', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('Three pages'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  // Recently used comes first once a type is picked, so a type's first button may be that one.
  const pick = (type: string) => design.locator('jfb-designer-palette').getByRole('button', { name: type }).first();
  const steps = design.getByRole('navigation', { name: 'Pages' });
  const fields = design.locator('jfb-canvas-field');
  await expect(design).toContainText('Add your first field');

  // Page 1: two fields from the palette, each after the one selected, then a third at the insert point between them.
  await pick('Short text').click();
  await pick('Email').click();
  await expect(fields).toHaveCount(2);
  await design.getByRole('button', { name: 'Add a field here, after Short text' }).click();
  await pick('Number').click();
  await expect(design.getByRole('button', { name: 'Number, number field' })).toHaveAttribute('aria-current', 'true');

  // Pages 2 and 3, each with a field.
  await steps.getByRole('button', { name: 'Add page' }).click();
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  await pick('Long text').click();
  await steps.getByRole('button', { name: 'Add page' }).click();
  await expect(steps.getByRole('button', { name: 'Page 3' })).toHaveAttribute('aria-current', 'step');
  await pick('Date').click();
  await expect(fields).toHaveCount(1);
  await axeInside(page, 'jfb-form-design-view');

  await steps.getByRole('button', { name: 'Page 1' }).click();
  await expect(fields).toHaveCount(3);
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect.poll(() => draftTypes(api, key)).toEqual([['text', 'number', 'email'], ['textarea'], ['date']]);
});

test('five fields come from a pasted list, each with the type its words suggest', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('Pasted'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: 'paste your questions' }).click();

  const dialog = page.locator('jfb-paste-questions-modal');
  await dialog
    .getByRole('textbox', { name: 'Your questions' })
    .fill('Your name\nEmail address\nHow many guests?\nDo you need parking?\n\nAnything else?');
  const rows = dialog.locator('tbody tr');
  await expect(rows).toHaveCount(5);
  await expect(rows.locator('td:first-child')).toHaveText([
    'Your name',
    'Email address',
    'How many guests?',
    'Do you need parking?',
    'Anything else?',
  ]);
  await axeInside(page, 'jfb-paste-questions-modal');
  await dialog.getByRole('button', { name: 'Add 5 fields' }).click();

  await expect(design.locator('jfb-canvas-field')).toHaveCount(5);
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect.poll(() => draftTypes(api, key)).toEqual([['text', 'email', 'number', 'yesno', 'textarea']]);
  expect(await draftLabels(api, key)).toEqual([
    'Your name',
    'Email address',
    'How many guests?',
    'Do you need parking?',
    'Anything else?',
  ]);
});

test('in the narrowest workspace the palette opens over the canvas, and a delete comes back with Undo', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const key = await contactForm(await apiAs('designer'), unique('Narrow'));
  // A 920 px window leaves the workspace 620 px beside Umbraco's tree.
  await page.setViewportSize({ width: 920, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const palette = design.locator('jfb-designer-palette');
  const fields = design.locator('jfb-canvas-field');
  await expect(fields).toHaveCount(3);
  await expect(palette).toHaveCount(0);

  // The skip link takes the keyboard past the palette to the form.
  const skip = design.getByRole('button', { name: 'Skip to the form' });
  await skip.focus();
  await page.keyboard.press('Enter');
  await expect(design.locator('jfb-form-canvas #jfb-canvas-form')).toBeFocused();

  // Add field opens the palette over the canvas; Esc closes it, and a pick adds the field and closes it.
  await design.getByRole('button', { name: 'Add field' }).click();
  await expect(palette).toHaveAttribute('mode', 'overlay');
  await expect(palette.getByRole('searchbox', { name: 'Search fields' })).toBeFocused();
  await axeInside(page, 'jfb-form-design-view');
  await page.keyboard.press('Escape');
  await expect(palette).toHaveCount(0);
  await design.getByRole('button', { name: 'Add field' }).click();
  await palette.getByRole('button', { name: 'Phone' }).first().click();
  await expect(palette).toHaveCount(0);
  await expect(fields).toHaveCount(4);

  // Delete says so with Undo, which brings the field back.
  await design.getByRole('button', { name: /^Email address, / }).click();
  await design.getByRole('button', { name: 'Delete Email address' }).click();
  await expect(fields).toHaveCount(3);
  await page
    .locator('uui-toast-notification')
    .filter({ hasText: 'Field deleted.' })
    .getByRole('button', { name: 'Undo' })
    .click();
  await expect(fields).toHaveCount(4);
  await expect(design.getByRole('button', { name: /^Email address, / })).toBeVisible();
});

test("a form trashed from its own header leaves its screen for its folder's", async ({ page, loginAs, apiAs }) => {
  const name = unique('Trashed here');
  const key = await contactForm(await apiAs('designer'), name);
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  await expect(nameField(page)).toHaveValue(name);

  await page.locator('umb-workspace-entity-action-menu').getByRole('button', { name: 'Actions' }).click();
  await page.locator('umb-entity-action-list uui-menu-item[label="Trash"]').filter({ visible: true }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-library-root$/);
  await expect(treeRow(page, name)).toHaveCount(0);
});

test("a field's result of the header search opens the form with that field selected", async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const name = unique('Membership');
  const label = unique('Membership number');
  const key = await contactForm(api, name);
  const field = await publishWithLabel(api, key, label);
  await loginAs('designer');
  await page.goto(SECTION);

  await page.locator('umb-backoffice-header-apps [label="Search"], umb-search-header-app uui-button').first().click();
  const search = page.locator('umb-search-modal');
  await search.getByRole('button', { name: 'Forms' }).click();
  await search.locator('input').fill(label);
  await search.locator('a', { hasText: `${label}, in ${name}` }).click();

  await expect(page).toHaveURL(new RegExp(`jfb-form/edit/${key}.*\\?field=${field}`));
  // The button that selects the field lies over it, named with its label.
  const selected = page.locator('jfb-form-design-view [aria-current="true"]');
  await expect(selected).toHaveCount(1);
  await expect(selected).toHaveAttribute('aria-label', new RegExp(`^${label}, `));
  await expect(selected).toBeInViewport();
});

test('who may only read entries gets the entry views, no designer, and a name they cannot change', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const name = unique('Read only');
  const key = await contactForm(await apiAs('designer'), name);
  await loginAs('viewer');
  await page.goto(workspaceOf(key));

  await expect(nameField(page)).toHaveValue(name);
  await expect(nameField(page)).not.toBeEditable();
  await expect(page.getByRole('tab', { name: 'Entries' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Insights' })).toBeVisible();
  for (const view of ['Design', 'Settings', 'Actions', 'History'])
    await expect(page.getByRole('tab', { name: view })).toHaveCount(0);
  for (const action of ['Save draft', 'Publish\u2026'])
    await expect(page.getByRole('button', { name: action })).toHaveCount(0);
  await expect(page.locator('jfb-coming-screen')).toBeVisible();
});
