import { readFileSync, writeFileSync } from 'node:fs';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import {
  API,
  blankForm,
  contactForm,
  created,
  formOf,
  KEY,
  publishWithLabel,
  unique,
} from '../../support/forms-api.ts';
import { openSearch } from '../../support/search.ts';
import { loadedIn, openRowActions, shownRow, treeRow } from '../../support/tree.ts';

// The form library (ROADMAP 9.2, ADR-0166): new forms blank, from the gallery (also from the Overview's checklist) and by import
// with its notes shown; export; folders made, renamed and deleted; a form moved; a restricted form's duplicate; actions copied
// with the references the target lacks; a form found by one of its field labels; and, one after another because they share the
// Recycle Bin, trash with Undo, a form picked on a page named by both confirmations, restore, a name shown as text in a
// confirm, and the emptied bin. Each test makes its own forms and pages, as the host is shared with every other spec.

const SECTION = '/umbraco/section/jfb-forms';
const MANAGEMENT_API = '/umbraco/management/api/v1';
/** The demo's volunteer page, whose copy picks a test's form, and the home page the copy goes below. */
const VOLUNTEER_PAGE = 'b7d3e0a1-5c94-4f2e-8a6b-0d1c3f9e2a47';
const HOME_PAGE = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';

const newFolder = async (api: APIRequestContext, name: string) =>
  created(await api.post(`${API}/folder`, { data: { name } }), `the folder ${name}`);

/** The key of the demo's form with this alias, as the search finds it. */
async function formWithAlias(api: APIRequestContext, alias: string): Promise<string> {
  const found = (await (await api.get(`${API}/form/search`, { params: { text: alias, take: 50 } })).json()) as {
    items: { key: string; alias?: string | null }[];
  };
  const key = found.items.find((item) => item.alias === alias)?.key;
  expect(key, `the demo's form ${alias}`).toMatch(KEY);
  return key as string;
}

async function trash(api: APIRequestContext, key: string): Promise<void> {
  const { concurrencyVersion } = await formOf(api, key);
  const response = await api.post(`${API}/form/${key}/trash`, { data: { concurrencyVersion } });
  expect(response.ok(), await response.text()).toBe(true);
}

/**
 * A page of its own that picks the form: a copy of the demo's volunteer page that Umbraco makes (so its blocks get keys of their
 * own), whose form block then names this form; the seeded pages stay as they are.
 */
async function pageThatPicks(api: APIRequestContext, formKey: string, name: string): Promise<void> {
  const copied = await api.post(`${MANAGEMENT_API}/document/${VOLUNTEER_PAGE}/copy`, {
    data: { target: { id: HOME_PAGE }, relateToOriginal: false, includeDescendants: false },
  });
  expect(copied.status(), await copied.text()).toBe(201);
  const pageKey = copied.headers()['umb-generated-resource'] ?? copied.headers()['location']?.split('/').pop() ?? '';
  expect(pageKey, 'the copied page').toMatch(KEY);
  const page = (await (await api.get(`${MANAGEMENT_API}/document/${pageKey}`)).json()) as {
    template: { id: string } | null;
    values: unknown[];
    variants: { culture: string | null; segment: string | null }[];
  };
  // The one key in the copied values that names a form is the volunteer form's; every other key stays.
  const values = JSON.stringify(page.values);
  const keys = [...new Set(values.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi) ?? [])];
  const forms: string[] = [];
  for (const key of keys) if ((await api.get(`${API}/form/${key}`)).status() === 200) forms.push(key);
  expect(forms, 'the volunteer form, picked in the copy').toHaveLength(1);
  const updated = await api.put(`${MANAGEMENT_API}/document/${pageKey}`, {
    data: {
      template: page.template,
      values: JSON.parse(values.split(forms[0] as string).join(formKey)) as unknown[],
      variants: page.variants.map((variant) => ({ culture: variant.culture, segment: variant.segment, name })),
    },
  });
  expect(updated.ok(), await updated.text()).toBe(true);
  // The "used on" index follows the save.
  await expect
    .poll(
      async () => ((await (await api.get(`${API}/form/${formKey}/referenced-by`)).json()) as { total: number }).total,
    )
    .toBe(1);
}

/** Chooses an action in the menu a "..." opened; a menu opened before may linger hidden. */
const chooseAction = (page: Page, action: string) =>
  page.locator(`umb-entity-action-list uui-menu-item[label="${action}"]`).filter({ visible: true }).click();

/** Opens a tree item's "..." and chooses an action. */
async function treeAction(page: Page, name: string, action: string): Promise<void> {
  await openRowActions(page, name);
  await chooseAction(page, action);
}

/** Opens the "+" of the sidebar's header, or of a folder in the tree, and chooses an option of Umbraco's create dialog. */
async function createIn(page: Page, place: string | null, option: string): Promise<void> {
  if (place === null) {
    await page.locator('umb-section-sidebar').getByRole('button', { name: 'Create' }).first().click();
  } else {
    const row = await shownRow(page, place);
    await row.hover();
    await row.getByRole('button', { name: 'Create' }).click();
  }
  await page.locator(`umb-entity-create-option-action-list-modal umb-ref-item[name="${option}"]`).click();
}

async function openSection(page: Page): Promise<void> {
  await page.goto(SECTION);
  await expect(page.locator('umb-section-sidebar')).toBeVisible();
}

/** Opens a folder in the tree, once: Umbraco opens the folder of a form it shows by itself, which a second click would close. */
async function expandInTree(page: Page, name: string): Promise<void> {
  const collapse = page.getByRole('button', { name: `Collapse child items for ${name}`, exact: true });
  await expect(async () => {
    if (await collapse.count()) return;
    const expand = page.getByRole('button', { name: `Expand child items for ${name}`, exact: true });
    if (await expand.count()) await expand.click();
    await expect(collapse).toBeVisible({ timeout: 1000 });
  }).toPass();
}

/** Chooses an item in Umbraco's tree picker and confirms it. */
async function pickInTree(page: Page, name: string): Promise<void> {
  const picker = page.locator('umb-tree-picker-modal');
  await (await loadedIn(picker, picker.locator(`uui-menu-item[label="${name}"]`))).click();
  await picker.getByRole('button', { name: 'Choose' }).click();
}

test("the Overview's Choose a template\u2026 opens the gallery, whose template becomes a form", async ({
  page,
  loginAs,
}) => {
  // The checklist's form step stays open whatever the shared host holds, so its button is there to press.
  await page.route(`**${API}/setup/checklist`, async (route) => {
    try {
      const response = await route.fetch();
      const body = (await response.json()) as { complete: boolean; items: { step: string; done: boolean }[] };
      body.complete = false;
      for (const item of body.items) item.done = item.step !== 'form';
      await route.fulfill({ response, json: body });
    } catch (error) {
      // A request the page gave up on, as a navigation does, is answered by nobody: its response is gone.
      if (!/disposed|closed/i.test(String(error))) throw error;
    }
  });
  await loginAs('designer');
  await openSection(page);
  await page.locator('jfb-overview').getByRole('button', { name: 'Choose a template\u2026' }).click();

  const gallery = page.locator('jfb-template-gallery-modal');
  await expect(gallery.getByRole('radio', { name: /^Contact/ })).toHaveAttribute('aria-checked', 'true');
  await expect(gallery.locator('jfb-form-preview .jfb-label')).toHaveText(['Name', 'Email address', 'Message']);
  await gallery.getByRole('button', { name: 'Use template' }).click();
  await expect(page).toHaveURL(/section\/jfb-forms\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/);
});

test('axe finds nothing in a folder, an empty folder, the gallery or the import sidebar', async ({
  page,
  loginAs,
  apiAs,
}, testInfo) => {
  const api = await apiAs('designer');
  const full = unique('Axe forms');
  const empty = unique('Axe empty');
  const fullKey = await newFolder(api, full);
  const emptyKey = await newFolder(api, empty);
  const source = await contactForm(api, unique('Axe survey'), fullKey);
  await blankForm(api, unique('Axe draft'));
  const exported = await api.get(`${API}/form/${source}/export`);
  const file = (await exported.json()) as { definition: { settings: { appearance?: Record<string, unknown> } } };
  file.definition.settings.appearance = { ...file.definition.settings.appearance, theme: 'midnight' };
  const path = testInfo.outputPath('axe.json');
  writeFileSync(path, JSON.stringify(file));
  await loginAs('designer');

  await page.goto(`${SECTION}/workspace/jfb-folder/edit/${fullKey}`);
  await expect(page.locator('jfb-folder-list table')).toBeVisible();
  await axeInside(page, 'jfb-folder-list');

  await page.goto(`${SECTION}/workspace/jfb-folder/edit/${emptyKey}`);
  await expect(page.locator('jfb-folder-list').getByRole('button', { name: 'Choose a template\u2026' })).toBeVisible();
  await axeInside(page, 'jfb-folder-list');

  await page.locator('jfb-folder-list').getByRole('button', { name: 'Choose a template\u2026' }).click();
  const gallery = page.locator('jfb-template-gallery-modal');
  await expect(gallery.locator('jfb-form-preview')).toBeVisible();
  await axeInside(page, 'jfb-template-gallery-modal');
  await gallery.getByRole('button', { name: 'Cancel' }).click();

  await openSection(page);
  await createIn(page, empty, 'Import a form\u2026');
  const sidebar = page.locator('jfb-import-modal');
  const chooser = page.waitForEvent('filechooser');
  await sidebar.getByRole('button', { name: 'Choose a file\u2026' }).click();
  await (await chooser).setFiles(path);
  await expect(sidebar).toContainText('What this site lacks');
  await axeInside(page, 'jfb-import-modal');
});

test("a blank form from a folder's + opens, and waits in the tree unpublished", async ({ page, loginAs, apiAs }) => {
  const folder = unique('Blank forms');
  await newFolder(await apiAs('designer'), folder);
  await loginAs('designer');
  await openSection(page);

  await createIn(page, folder, 'Form');
  await expect(page).toHaveURL(/section\/jfb-forms\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/);
  await expandInTree(page, folder);
  const row = treeRow(page, 'Untitled form').first();
  await expect(row.locator('.unpublished')).toHaveText('Untitled form, not published');
});

test('a long name ends with an ellipsis before the "..." of its row, which a click then reaches', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const name = unique('A form whose name runs far past the width of the sidebar, as some editors write them');
  await blankForm(await apiAs('designer'), name);
  await loginAs('designer');
  await openSection(page);
  const row = await shownRow(page, name);
  await row.evaluate((element) => element.scrollIntoView({ block: 'center' }));
  await row.hover();
  const more = page.getByRole('button', { name: `View actions for '${name}'`, exact: true });
  await expect(more).toBeVisible();
  // A name drawn past its box would lie over the "...", and a click there would open the form instead.
  const label = row.locator('span[slot="label"]');
  const named = (await label.boundingBox())!;
  const button = (await more.boundingBox())!;
  expect(named.x + named.width, 'the name ends before the "..."').toBeLessThanOrEqual(button.x);
  expect(
    await label.evaluate((element) => [
      element.scrollWidth > element.clientWidth,
      getComputedStyle(element).textOverflow,
    ]),
  ).toEqual([true, 'ellipsis']);
  await more.click();
  await expect(page.locator('umb-entity-action-list').filter({ visible: true })).toBeVisible();
});

test('a folder is made, renamed in its own name, and deleted once empty', async ({ page, loginAs }) => {
  const name = unique('Campaigns');
  const renamed = `${name} 2027`;
  await loginAs('designer');
  await openSection(page);

  await createIn(page, null, 'Folder\u2026');
  const naming = page.locator('jfb-folder-name-modal');
  await naming.getByRole('textbox', { name: 'Name' }).fill(name);
  await naming.getByRole('button', { name: 'Create folder' }).click();
  await expect(await shownRow(page, name)).toBeVisible();

  await treeAction(page, name, 'Rename\u2026');
  await expect(naming.getByRole('textbox', { name: 'Name' })).toHaveValue(name);
  await naming.getByRole('textbox', { name: 'Name' }).fill(renamed);
  await naming.getByRole('button', { name: 'Rename' }).click();
  await expect(await shownRow(page, renamed)).toBeVisible();

  // Its screen's name renames it on Enter, and deleting it from there opens the screen of the place it was in.
  const again = `${renamed} B`;
  await treeRow(page, renamed).click();
  const field = page.locator('jfb-folder-list').getByRole('textbox', { name: 'Folder name' });
  await expect(field).toHaveValue(renamed);
  await field.fill(again);
  await field.press('Enter');
  await expect(await shownRow(page, again)).toBeVisible();
  await expect(treeRow(page, renamed)).toHaveCount(0);

  await page
    .locator('jfb-folder-list')
    .getByRole('button', { name: `More actions for ${again}` })
    .click();
  await chooseAction(page, 'Delete\u2026');
  const confirm = page.locator('umb-confirm-modal');
  await expect(confirm).toContainText(`Delete ${again}?`);
  await confirm.getByRole('button', { name: 'Delete folder' }).click();
  await expect(treeRow(page, again)).toHaveCount(0);
  await expect(page).toHaveURL(/\/workspace\/jfb-library-root$/);
});

test('a form moves into a folder chosen in the tree picker', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const form = unique('Moving form');
  const folder = unique('Moved here');
  const formKey = await blankForm(api, form);
  const folderKey = await newFolder(api, folder);
  await loginAs('designer');
  await openSection(page);

  await treeAction(page, form, 'Move to\u2026');
  await pickInTree(page, folder);
  await expect.poll(async () => (await formOf(api, formKey)).folderKey).toBe(folderKey);
  await expandInTree(page, folder);
  await expect(treeRow(page, form)).toBeVisible();
});

test("a restricted form's Duplicate dialog says that the copy takes its folder's access", async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('admin');
  const name = unique('Salary review');
  const key = await contactForm(api, name);
  const restricting = await api.post('/jfb-testing/form-restriction', { data: { form: key, restricted: true } });
  expect(restricting.status()).toBe(204);
  await loginAs('admin');
  await openSection(page);

  await treeAction(page, name, 'Duplicate\u2026');
  const dialog = page.locator('jfb-duplicate-modal');
  // The headline names the form; the sentence below says "this form".
  await expect(dialog).toContainText(
    "The copy doesn't take this form's own access settings: it gets the access set in the top level of Forms.",
  );
  expect((await dialog.locator('uui-dialog-layout').boundingBox())?.width ?? 0).toBeLessThanOrEqual(520);
  await expect(dialog.getByRole('textbox', { name: 'Name of the copy' })).toHaveValue(`${name} (copy)`);
  await axeInside(page, 'jfb-duplicate-modal');
  await dialog.getByRole('button', { name: 'Duplicate', exact: true }).click();
  await expect(page).toHaveURL(/section\/jfb-forms\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/);
  await expect(await shownRow(page, `${name} (copy)`)).toBeVisible();
});

test('actions copied to a form without their fields list each reference with a link to fix it', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const target = unique('Empty target');
  const targetKey = await blankForm(api, target);
  // A copy of the demo's contact form, with its two emails that name its fields and use their merge tags.
  const source = unique('Enquiries');
  const duplicated = await api.post(`${API}/form/${await formWithAlias(api, 'contact')}/duplicate`, {
    data: { name: source, includeActions: true },
  });
  await created(duplicated, `the form ${source}`);
  await loginAs('designer');
  await openSection(page);

  await treeAction(page, source, 'Copy actions to\u2026');
  const sidebar = page.locator('jfb-copy-actions-modal');
  // A headline short enough that Umbraco's one-line header never cuts it; the form copied from heads its actions.
  await expect(sidebar.locator('umb-body-layout')).toHaveAttribute('headline', 'Copy actions');
  await expect(sidebar.locator(`uui-box[headline="Actions from ${source}"]`)).toBeVisible();
  await sidebar.getByRole('button', { name: 'Choose a form\u2026' }).click();
  await pickInTree(page, target);
  await expect(sidebar).toContainText(target);
  await sidebar.getByRole('button', { name: 'Copy 2 actions' }).click();

  // The form copied to is named once; what each action lacks is listed under it.
  await expect(sidebar).toContainText(`2 actions copied to ${target}.`);
  await expect(sidebar).toContainText("They name things that form doesn't have. Fix them before you publish the form:");
  const after = (await sidebar.locator('.result ~ *').allTextContents()).join(' ');
  expect(after, 'the form copied to, named once above').not.toContain(target);
  // Said once for each action, however often an action names the field.
  await expect(sidebar.getByText('The field "email"', { exact: true })).toHaveCount(2);
  await expect(sidebar).toContainText('The merge tag {{ field.name }}');
  await axeInside(page, 'jfb-copy-actions-modal');
  const fixes = sidebar.getByRole('link', { name: 'Fix it in the form' });
  await expect(fixes).toHaveCount(2);
  await expect(fixes.first()).toHaveAttribute('href', new RegExp(`jfb-form/edit/${targetKey}\\?action=[0-9a-f-]{36}$`));

  // The link opens the form copied to at its Actions view, the focus on the copy's card (ROADMAP 10.2a, ADR-0188).
  const copy = /action=([0-9a-f-]{36})$/.exec((await fixes.first().getAttribute('href')) ?? '')?.[1];
  const draft = (await (await api.get(`${API}/form/${targetKey}/draft`)).json()) as {
    definition: { actions?: { key: string; name?: string }[] };
  };
  const name = draft.definition.actions?.find((action) => action.key === copy)?.name ?? '';
  expect(name, 'the copy, by the key its link names').not.toBe('');
  await fixes.first().click();
  await expect(sidebar).toHaveCount(0);
  await expect(page).toHaveURL(new RegExp(`jfb-form/edit/${targetKey}/view/actions$`));
  const actions = page.locator('jfb-form-actions-view');
  await expect(actions.locator('.open').filter({ has: page.locator('.name', { hasText: name }) })).toBeFocused();
});

test('an import shows what the file holds and the theme this site lacks, then arrives as a new copy', async ({
  page,
  loginAs,
  apiAs,
}, testInfo) => {
  const api = await apiAs('designer');
  const folder = unique('Imports');
  await newFolder(api, folder);
  const source = unique('Shared survey');
  const exported = await api.get(`${API}/form/${await contactForm(api, source)}/export`);
  expect(exported.ok()).toBe(true);
  const file = (await exported.json()) as { definition: { settings: { appearance?: Record<string, unknown> } } };
  file.definition.settings.appearance = { ...file.definition.settings.appearance, theme: 'midnight' };
  const good = testInfo.outputPath('shared-survey.json');
  writeFileSync(good, JSON.stringify(file, null, 2));
  const broken = testInfo.outputPath('broken.json');
  writeFileSync(broken, JSON.stringify({ ...file, definition: { ...file.definition, unknownSetting: true } }));
  await loginAs('designer');
  await openSection(page);

  await createIn(page, folder, 'Import a form\u2026');
  const sidebar = page.locator('jfb-import-modal');
  let chooser = page.waitForEvent('filechooser');
  await sidebar.getByRole('button', { name: 'Choose a file\u2026' }).click();
  await (await chooser).setFiles(broken);
  await expect(sidebar.getByRole('alert')).toContainText("This file can't be imported:");

  chooser = page.waitForEvent('filechooser');
  await sidebar.getByRole('button', { name: 'Choose another file\u2026' }).click();
  await (await chooser).setFiles(good);
  await expect(sidebar).toContainText(source);
  // Each count once, after its label.
  await expect(sidebar.locator('dt:text-is("Pages") + dd')).toHaveText('1');
  await expect(sidebar.locator('dt:text-is("Fields") + dd')).toHaveText('3');
  await expect(sidebar).toContainText('What this site lacks');
  await expect(sidebar).toContainText('This site has no theme "midnight", so the form uses the default theme.');
  await expect(sidebar).toContainText(`It arrives as a draft in ${folder}`);
  await sidebar.getByRole('button', { name: 'Import as a new copy' }).click();
  await expect(page).toHaveURL(/section\/jfb-forms\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/);
  await expandInTree(page, folder);
  // The copy sits in the folder; the form the file came from stays at the top level, read as far as its row.
  await expect(treeRow(page, folder).locator(`uui-menu-item[label="${source}"]`)).toBeVisible();
  await loadedIn(page.locator('umb-section-sidebar'), treeRow(page, source).nth(1));
  await expect(treeRow(page, source)).toHaveCount(2);
});

test('Export as JSON downloads the form as an export file', async ({ page, loginAs, apiAs }) => {
  const name = unique('Exported form');
  await contactForm(await apiAs('designer'), name);
  await loginAs('designer');
  await openSection(page);

  const downloading = page.waitForEvent('download');
  await treeAction(page, name, 'Export as JSON');
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/\.json$/);
  const file = JSON.parse(readFileSync(await download.path(), 'utf8')) as { format: string; form: { name: string } };
  expect(file.format).toBe('jfb-form-export/1');
  expect(file.form.name).toBe(name);
});

test("Replace from a file\u2026 in an open form says unsaved changes go, then the file is the form's draft", async ({
  page,
  loginAs,
  apiAs,
}, testInfo) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const name = unique('Replaced form');
  const key = await contactForm(api, name);
  const exported = await api.get(`${API}/form/${key}/export`);
  expect(exported.ok()).toBe(true);
  const file = (await exported.json()) as {
    definition: { pages: { groups: { fields: { label: Record<string, string> }[] }[] }[] };
  };
  const first = file.definition.pages[0]?.groups[0]?.fields[0];
  if (!first) throw new Error('The Contact template has a field.');
  first.label = { '': 'Your full name' };
  const replacement = testInfo.outputPath('replacement.json');
  writeFileSync(replacement, JSON.stringify(file, null, 2));
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/design`);
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: /^Email address, / }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill('Work email');

  await page.locator('umb-workspace-entity-action-menu').getByRole('button', { name: 'Actions' }).click();
  await chooseAction(page, 'Replace from a file\u2026');
  const sidebar = page.locator('jfb-import-modal');
  await expect(sidebar.locator('umb-body-layout')).toHaveAttribute('headline', `Replace ${name} from a file`);
  const chooser = page.waitForEvent('filechooser');
  await sidebar.getByRole('button', { name: 'Choose a file\u2026' }).click();
  await (await chooser).setFiles(replacement);
  await expect(sidebar).toContainText(
    `It becomes the draft of ${name}. Visitors keep seeing the published version until you publish. Changes you haven't saved yet are discarded.`,
  );
  await axeInside(page, 'jfb-import-modal');
  await sidebar.getByRole('button', { name: 'Replace the draft' }).click();

  await expect(sidebar).toHaveCount(0);
  await expect(design.getByRole('button', { name: /^Your full name, / })).toBeVisible();
  await expect(design.getByRole('button', { name: /^Email address, / })).toBeVisible();
  await expect(
    page.locator('uui-toast-notification').filter({ hasText: `The draft of ${name} is replaced.` }),
  ).toBeVisible();
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as { form: { name: string } };
  expect(draft.form.name).toBe(name);
});

test('the header search finds a form by one of its field labels and opens it with that field', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const name = unique('Searched form');
  const label = unique('Membership number');
  const key = await contactForm(api, name);
  const field = await publishWithLabel(api, key, label);
  await loginAs('designer');
  await openSection(page);

  const search = await openSearch(page);
  await search.locator('input').fill(label);
  const result = search.locator('a', { hasText: `${label}, in ${name}` });
  await expect(result).toHaveAttribute('href', new RegExp(`jfb-form/edit/${key}\\?field=${field}$`));
});

test.describe('the Recycle Bin', () => {
  // Emptying the bin removes what every test put there, so the tests that trash run one after another, the emptying last.
  test.describe.configure({ mode: 'serial' });

  const BIN = `${SECTION}/workspace/jfb-recycle-bin`;

  /** Opens a row's "..." on the Recycle Bin's screen and chooses an action. */
  async function binAction(page: Page, name: string, action: string): Promise<void> {
    await page
      .locator('jfb-recycle-bin')
      .getByRole('button', { name: `More actions for ${name}` })
      .click();
    await chooseAction(page, action);
  }

  test('a form no page picks goes at once, and Undo brings it back', async ({ page, loginAs, apiAs }) => {
    const name = unique('Raffle');
    await blankForm(await apiAs('designer'), name);
    await loginAs('designer');
    await openSection(page);

    await treeAction(page, name, 'Trash');
    await expect(page.locator('umb-confirm-modal')).toHaveCount(0);
    await expect(treeRow(page, name)).toHaveCount(0);
    const toast = page.locator('jfb-undo-notification');
    await expect(toast).toContainText(`${name} moved to the Recycle Bin.`);
    await toast.getByRole('button', { name: 'Undo' }).click();
    // The tree reads its first page again, which the row can fall past late in a run: it is read as far as the row.
    await expect(await shownRow(page, name)).toBeVisible();
  });

  test('both confirmations name the page that picks a form, and Restore brings it back', async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const api = await apiAs('admin');
    const name = unique('Open day');
    const pageName = unique('Open day page');
    const key = await contactForm(api, name);
    await pageThatPicks(api, key, pageName);
    await loginAs('admin');
    await openSection(page);

    await treeAction(page, name, 'Trash');
    const confirm = page.locator('umb-confirm-modal');
    await expect(confirm).toContainText(`Move ${name} to the Recycle Bin?`);
    await expect(confirm).toContainText(
      `It's picked on 1 page: ${pageName}. That page shows nothing in its place until you restore the form.`,
    );
    await confirm.getByRole('button', { name: 'Move to Recycle Bin' }).click();
    await expect(treeRow(page, name)).toHaveCount(0);

    await page.goto(BIN);
    await binAction(page, name, 'Delete\u2026');
    await expect(confirm).toContainText(`Delete ${name} for good?`);
    await expect(confirm).toContainText(
      `It's still picked on 1 page: ${pageName}. It keeps showing nothing in its place.`,
    );
    await confirm.getByRole('button', { name: 'Cancel' }).click();

    await binAction(page, name, 'Restore\u2026');
    const restoring = page.locator('jfb-restore-modal');
    await expect(restoring).toContainText(`${name} comes back at the top level of Forms.`);
    await axeInside(page, 'jfb-restore-modal');
    await restoring.getByRole('button', { name: 'Restore', exact: true }).click();
    await expect(page.locator('jfb-recycle-bin tr', { hasText: name })).toHaveCount(0);
    // The row and its menu are gone; the keyboard goes on from the bin's heading, not from the page's start.
    await expect(page.locator('jfb-recycle-bin h2')).toBeFocused();
    // The tree has the form again, read as far as its row, which takes the focus along.
    await expect(await shownRow(page, name)).toBeVisible();
  });

  test('a confirm that names a form shows the name as text, and nothing in it runs', async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const name = '<img src=x onerror=alert(1)>';
    const api = await apiAs('admin');
    await trash(api, await blankForm(api, name));
    const alerts: string[] = [];
    page.on('dialog', (dialog) => {
      alerts.push(dialog.message());
      void dialog.dismiss();
    });
    await loginAs('admin');
    await page.goto(BIN);
    await expect(page.locator('jfb-recycle-bin tr', { hasText: name })).toBeVisible();
    await axeInside(page, 'jfb-recycle-bin');

    await binAction(page, name, 'Delete\u2026');
    const confirm = page.locator('umb-confirm-modal');
    await expect(confirm).toContainText(`Delete ${name} for good?`);
    await expect(confirm.locator('img')).toHaveCount(0);
    await confirm.getByRole('button', { name: 'Delete form' }).click();
    await expect(page.locator('jfb-recycle-bin tr', { hasText: name })).toHaveCount(0);
    expect(alerts).toEqual([]);
  });

  test('Empty recycle bin\u2026 says what goes for good, then leaves the bin empty', async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const api = await apiAs('admin');
    await trash(api, await blankForm(api, unique('Old poll')));
    await loginAs('admin');
    await page.goto(BIN);

    await page.locator('jfb-recycle-bin').getByRole('button', { name: 'Empty recycle bin\u2026' }).click();
    const confirm = page.locator('umb-confirm-modal');
    await expect(confirm).toContainText('Empty the Recycle Bin?');
    await expect(confirm).toContainText(/This deletes \d+ forms? for good\./);
    await expect(confirm).toContainText("You can't undo this.");
    await confirm.getByRole('button', { name: 'Empty recycle bin' }).click();
    await expect(page.locator('jfb-recycle-bin')).toContainText('The Recycle Bin is empty.');
  });
});
