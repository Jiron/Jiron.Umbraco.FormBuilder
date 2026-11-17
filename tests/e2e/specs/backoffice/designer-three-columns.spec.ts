import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { inLanguage } from '../../support/backoffice-look.ts';
import { pageMenu, pagesOf, tabOf } from '../../support/designer-row.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';
import {
  addFieldOf,
  openPicker,
  openPickerByKeyboard,
  pick,
  pickByKeyboard,
  pickerClosed,
  pickerOf,
} from '../../support/picker.ts';

// The designer in three columns (ROADMAP 22.1, ADR-0256), each check of the step's "Done when" a test that fails
// without it: a field added through the picker at a page's end and between two fields, by mouse and by keyboard alone,
// at a 1440 px and a 920 px window, landing where it was asked for, selected, and Esc giving the focus back; no list of
// types beside the form, one control that adds a page and one that offers Change type; a page renamed, duplicated and
// deleted at its tab, an only page without Delete, Undo bringing a deleted page back with its fields; the open page in
// the inspector while nothing is selected, the form's left edge and width the same whatever is selected; and the form's
// column wider than half the window under one row of controls, in English and German, and the full 960 px of Desktop
// at 1920 px. After sending's own checks are in form-outcomes.spec.ts.

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

const text = (alias: string, label: string) => ({
  key: crypto.randomUUID(),
  type: 'text',
  alias,
  label: { '': label },
});

/** A blank form whose pages are these, by title, each holding the fields named. */
async function formOfPages(api: APIRequestContext, pages: readonly (readonly [string, readonly string[]])[]) {
  const key = await blankForm(api, unique('Columns'));
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: unknown[] };
  };
  draft.definition.pages = pages.map(([title, labels]) => ({
    key: crypto.randomUUID(),
    title: { '': title },
    groups: [
      { key: crypto.randomUUID(), fields: labels.map((label) => text(label.toLowerCase().replace(/\W/g, ''), label)) },
    ],
  }));
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return key;
}

async function openDesign(page: Page, key: string): Promise<Locator> {
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field').first()).toBeVisible({ timeout: 60_000 });
  return design;
}

/** The labels the canvas's fields show on the page shown, in order, as their buttons name them. */
const labelsOf = (design: Locator) =>
  design
    .locator('jfb-canvas-field')
    .evaluateAll((all) =>
      all.map(
        (each) => each.querySelector(':scope > .bo-chrome > .bo-select')?.getAttribute('aria-label')?.split(',')[0],
      ),
    );

/** The selected field's button. */
const selectedOf = (design: Locator) => design.locator('jfb-form-canvas [data-control="select"][aria-current="true"]');

/** Over a narrow canvas a selection's inspector lies on top: Esc gives the canvas back. */
async function backToCanvas(page: Page, design: Locator) {
  if ((await design.locator('jfb-designer-inspector[mode="overlay"]').count()) === 0) return;
  await page.keyboard.press('Escape');
  await expect(design.locator('jfb-designer-inspector[mode="overlay"]')).toHaveCount(0);
}

for (const width of [1440, 920] as const) {
  test(`at a ${width} px window a field is added through the picker by mouse, at a page's end and between two fields`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const key = await formOfPages(await apiAs('designer'), [['Your details', ['First', 'Second']]]);
    await page.setViewportSize({ width, height: 900 });
    await loginAs('designer');
    const design = await openDesign(page, key);

    // Esc lands nothing and gives the focus back to Add field, which opened the picker.
    await openPicker(page, addFieldOf(design));
    await axeInside(page, 'jfb-field-picker-modal');
    await page.keyboard.press('Escape');
    await pickerClosed(page);
    await expect(addFieldOf(design)).toBeFocused();
    expect(await labelsOf(design)).toEqual(['First', 'Second']);

    // At the page's end, selected.
    await pick(page, addFieldOf(design), 'Email');
    await expect(selectedOf(design)).toHaveAttribute('aria-label', /^Email, /);
    expect(await labelsOf(design)).toEqual(['First', 'Second', 'Email']);
    await backToCanvas(page, design);

    // Between the first two fields, at the insert point under the first, selected.
    await pick(
      page,
      design.getByRole('button', { name: 'Add a field here, after First' }).locator('.bo-pill'),
      'Number',
    );
    await expect(selectedOf(design)).toHaveAttribute('aria-label', /^Number, /);
    expect(await labelsOf(design)).toEqual(['First', 'Number', 'Second', 'Email']);
  });

  test(`at a ${width} px window a field is added through the picker by keyboard alone, at a page's end and between two fields`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const key = await formOfPages(await apiAs('designer'), [['Your details', ['First', 'Second']]]);
    await page.setViewportSize({ width, height: 900 });
    await loginAs('designer');
    const design = await openDesign(page, key);
    const insert = design.getByRole('button', { name: 'Add a field here, after First' });

    // Esc gives the focus back to the insert point that opened the picker.
    await openPickerByKeyboard(page, insert);
    await page.keyboard.press('Escape');
    await pickerClosed(page);
    await expect(insert).toBeFocused();

    // At the page's end: the search typed, Enter on the match marked first; the new field selected with the focus.
    await pickByKeyboard(page, addFieldOf(design), 'Phone');
    await expect(selectedOf(design)).toHaveAttribute('aria-label', /^Phone, /);
    expect(await labelsOf(design)).toEqual(['First', 'Second', 'Phone']);
    await backToCanvas(page, design);

    // Between the first two fields.
    await pickByKeyboard(page, insert, 'Date');
    await expect(selectedOf(design)).toHaveAttribute('aria-label', /^Date, /);
    expect(await labelsOf(design)).toEqual(['First', 'Date', 'Second', 'Phone']);
    if (width === 1440) await expect(selectedOf(design)).toBeFocused();
    else await expect(design.locator('jfb-designer-inspector h2')).toBeFocused();
  });
}

test('the Design view holds no list of types beside the form, one control that adds a page and one that offers Change type', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const key = await formOfPages(await apiAs('designer'), [['Your details', ['First', 'Second']]]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await openDesign(page, key);
  // The site's field types, by the names the picker gives them.
  await openPicker(page, addFieldOf(design));
  const types = (await pickerOf(page).locator('.item > span').allTextContents()).map((name) => name.trim());
  expect(types).toContain('Short text');
  await page.keyboard.press('Escape');
  await pickerClosed(page);
  await design.locator('jfb-canvas-field [data-control="select"]').first().click();
  await expect(design.locator('jfb-designer-inspector h2')).toHaveText('Short text field');

  // Every control of the view, its shadow roots looked into; Umbraco's own controls counted once, as their hosts. The
  // width "Phone" shares a type's name, so the widths are left out.
  const names = await design.evaluate((view) => {
    const deep = (root: ParentNode): Element[] =>
      [...root.querySelectorAll('*')].flatMap((element) => [
        element,
        ...(element.shadowRoot && !element.localName.startsWith('uui-') ? deep(element.shadowRoot) : []),
      ]);
    return deep(view.shadowRoot!)
      .filter((element) => element.matches('button, uui-button, [role="menuitem"]') && !element.closest('.bo-widths'))
      .map((element) =>
        (element.getAttribute('aria-label') ?? element.getAttribute('label') ?? element.textContent ?? '').trim(),
      );
  });
  expect(names.filter((name) => types.includes(name))).toEqual([]);
  expect(names.filter((name) => name === 'Add page')).toHaveLength(1);
  expect(names.filter((name) => name === 'Change type\u2026')).toHaveLength(1);
});

test('a page is renamed, duplicated and deleted at its tab, an only page offers no Delete, and Undo brings a deleted page back with its fields', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await formOfPages(api, [
    ['Your details', ['First', 'Second']],
    ['Your message', ['Note']],
  ]);
  const only = await formOfPages(api, [['Alone', ['Field']]]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');

  // A form's only page: Rename and Duplicate, no Delete.
  let design = await openDesign(page, only);
  await pagesOf(design).getByRole('button', { name: 'More for Alone', exact: true }).click();
  const items = design.locator('jfb-form-canvas .bo-tab uui-popover-container:popover-open button');
  await expect(items).toHaveText(['Rename', 'Duplicate']);
  await page.keyboard.press('Escape');

  design = await openDesign(page, key);
  // Rename: the page's Title in the inspector, its text selected, typed over.
  await pageMenu(design, 'Your details', 'Rename');
  const title = design.locator('jfb-designer-inspector #prop-title');
  await expect(title).toBeFocused();
  await page.keyboard.type('About you');
  await expect(tabOf(design, 'About you')).toHaveAttribute('aria-current', 'step');

  // Duplicate: a copy after it, with its fields, shown.
  await pageMenu(design, 'About you', 'Duplicate');
  await expect(pagesOf(design).locator('.bo-tab')).toHaveCount(3);
  await expect(pagesOf(design).locator('.bo-tab').nth(1).locator('.bo-tab-name')).toHaveAttribute(
    'aria-current',
    'step',
  );
  expect(await labelsOf(design)).toEqual(['First', 'Second']);

  // Delete: the page and its fields go, with Undo in the toast, which brings them back.
  await tabOf(design, 'Your message').click();
  await pageMenu(design, 'Your message', 'Delete');
  await expect(pagesOf(design).locator('.bo-tab')).toHaveCount(2);
  const toast = page.locator('uui-toast-notification').filter({ hasText: 'Your message deleted.' });
  await toast.getByRole('button', { name: 'Undo' }).click();
  await expect(pagesOf(design).locator('.bo-tab')).toHaveCount(3);
  await tabOf(design, 'Your message').click();
  expect(await labelsOf(design)).toEqual(['Note']);
});

test('the inspector shows the open page while nothing is selected, and selecting and clearing a field moves the form not at all', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const key = await formOfPages(await apiAs('designer'), [['Your details', ['First', 'Second']]]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await openDesign(page, key);
  const inspector = design.locator('jfb-designer-inspector');
  const form = design.locator('jfb-form-canvas .bo-sheet .jfb-form');
  await expect(inspector.getByRole('heading', { name: 'Page', exact: true })).toBeVisible();
  await expect(inspector.locator('#prop-title')).toHaveValue('Your details');
  const edge = async () => {
    const box = (await form.boundingBox())!;
    return { left: Math.round(box.x), width: Math.round(box.width) };
  };
  const before = await edge();

  await design.getByRole('button', { name: /^Second, / }).click();
  await expect(inspector.getByRole('heading', { name: 'Short text field' })).toBeVisible();
  expect(await edge(), 'the form while a field is selected').toEqual(before);
  await page.keyboard.press('Escape');
  await expect(inspector.getByRole('heading', { name: 'Page', exact: true })).toBeVisible();
  expect(await edge(), 'the form once nothing is selected').toEqual(before);
});

/** The canvas's column, the row's two parts, and whether they share one line. */
const measure = (design: Locator) =>
  design.evaluate((view) => {
    const root = view.shadowRoot!;
    const canvas = root.querySelector('jfb-form-canvas')!;
    const inner = canvas.shadowRoot!;
    const pages = inner.querySelector('.bo-pages')!.getBoundingClientRect();
    const end = inner.querySelector('.bo-end')!.getBoundingClientRect();
    return { canvas: canvas.getBoundingClientRect().width, oneRow: end.top < pages.bottom, window: window.innerWidth };
  });

for (const language of ['en-US', 'de-DE'] as const) {
  test(`at 1440x900 the form's column is wider than half the window under one row of controls, the backoffice in ${language}`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const key = await formOfPages(await apiAs('designer'), [
      ['Your details', ['First']],
      ['Your message', ['Note']],
    ]);
    await page.setViewportSize({ width: 1440, height: 900 });
    if (language === 'de-DE') await inLanguage(page, language);
    await loginAs('designer');
    const design = await openDesign(page, key);
    // The site has two languages, so the row holds its language button too.
    await expect(design.locator('jfb-form-canvas .bo-language')).toBeVisible();
    if (language === 'de-DE')
      await expect(design.locator('jfb-form-canvas .bo-add-page')).toHaveAttribute('aria-label', /^Seite hinzuf/);
    const measured = await measure(design);
    expect(measured.canvas, "the form's column").toBeGreaterThan(measured.window / 2);
    expect(measured.oneRow, 'the pages and the tools on one row').toBe(true);
  });
}

test('at 1920x1080 Desktop shows the form at its full 960 px', async ({ page, loginAs, apiAs }) => {
  const key = await formOfPages(await apiAs('designer'), [['Your details', ['First']]]);
  await page.setViewportSize({ width: 1920, height: 1080 });
  await loginAs('designer');
  const design = await openDesign(page, key);
  await design.locator('jfb-form-canvas .bo-widths').getByRole('button', { name: 'Desktop' }).click();
  await expect(design.locator('jfb-form-canvas .bo-widths').getByRole('button', { name: 'Desktop' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  const form = (await design.locator('jfb-form-canvas .bo-sheet .jfb-form').boundingBox())!;
  expect(Math.round(form.width)).toBe(960);
  await expect(design.locator('jfb-form-canvas .bo-narrowed')).not.toHaveAttribute('data-shown', '');
});
