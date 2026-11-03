import type { Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, contactForm, formOf, publishWithLabel, unique } from '../../support/forms-api.ts';

// The form workspace (ROADMAP 9.3a, ADR-0166, docs/ux/information-architecture.md section 5): it loads a form's draft, renames
// the form in its header and saves through the API, publishes after asking, asks before leaving unsaved changes, opens a
// field's search result with that field selected, and shows each user the views and actions their permissions allow.

const SECTION = '/umbraco/section/jfb-forms';

const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

/** A form's row in the forms tree, by the name its label carries (a row may hold more text for screen readers). */
const treeRow = (page: Page, name: string) =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name}"]`).filter({ visible: true });

/** The form workspace's name field. */
const nameField = (page: Page) => page.locator('jfb-form-workspace-editor').getByRole('textbox', { name: 'Form name' });

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
  await expect(page.locator('jfb-form-design-view')).toContainText('No fields in this group yet.');
  await axeInside(page, 'jfb-form-design-view');
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
  const selected = page.locator('jfb-form-design-view [aria-current="true"]');
  await expect(selected).toHaveCount(1);
  await expect(selected).toContainText(label);
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
