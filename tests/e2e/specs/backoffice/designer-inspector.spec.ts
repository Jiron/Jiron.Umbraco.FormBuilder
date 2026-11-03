import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// The inspector (ROADMAP 9.4a, ADR-0170): every setting of the text, number and date types set with the plain editors
// and saved, axe clean with each section open; the palette folded beside it at a 1024 px window; a new page started
// from the selected field's "..." with the keyboard only; a field's German label edited on the site's two languages,
// with the mark of a field that has no German text; and an alias changed. The designer locks need a host of their own
// (designer-locks.spec.ts).

const SECTION = '/umbraco/section/jfb-forms';

const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

/** Whether an element holds the focus, followed into the shadow roots it lies in. */
const holdsFocus = (target: Locator) =>
  target.evaluate((element) => {
    let found = document.activeElement;
    while (found?.shadowRoot?.activeElement) found = found.shadowRoot.activeElement;
    return found === element;
  });

interface DraftField {
  readonly key: string;
  readonly alias?: string;
  readonly label?: Record<string, string>;
  readonly formerAliases?: string[];
  readonly settings?: Record<string, unknown>;
  readonly [other: string]: unknown;
}

/** The fields of a form's saved draft, by alias, and its pages. */
async function draftOf(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: DraftField[] }[] }[] };
  };
  const fields = new Map<string, DraftField>();
  for (const page of draft.definition.pages)
    for (const group of page.groups) for (const field of group.fields) fields.set(field.alias ?? field.key, field);
  return { pages: draft.definition.pages, fields };
}

/** Opens every section of the inspector and every "More options" in it. */
async function openAll(inspector: Locator) {
  // Each click takes a match away, so the first closed one is clicked until none is left.
  const closed = inspector.locator('.section-head button[aria-expanded="false"]');
  while ((await closed.count()) > 0) await closed.first().click();
  const more = inspector.locator('button.more[aria-expanded="false"]');
  while ((await more.count()) > 0) await more.first().click();
}

/** Types into a box and leaves it, as a person does. */
async function typeIn(box: Locator, value: string) {
  await box.fill(value);
  await box.press('Tab');
}

async function open(page: Page, key: string) {
  await page.goto(workspaceOf(key));
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

test('every setting of the text, number and date types is set with the plain editors, axe clean with each section open', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Inspector settings'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' } },
    { type: 'number', alias: 'guests', label: { '': 'Guests' } },
    { type: 'date', alias: 'arrival', label: { '': 'Arrival' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  // The text field: its lengths, the texts before and after, and the counter.
  await design.getByRole('button', { name: /^Your name, / }).click();
  await expect(inspector.getByRole('heading', { name: 'Short text field' })).toBeVisible();
  await openAll(inspector);
  await typeIn(inspector.getByLabel('Minimum length'), '2');
  await typeIn(inspector.getByLabel('Maximum length'), '9000');
  await expect(inspector.getByText('Enter 4000 or less.')).toBeVisible();
  await expect(inspector.getByLabel('Maximum length')).toHaveAttribute('aria-invalid', 'true');
  await typeIn(inspector.getByLabel('Maximum length'), '120');
  await expect(inspector.getByText('Enter 4000 or less.')).toHaveCount(0);
  await typeIn(inspector.getByLabel('Text before'), 'Dr.');
  await typeIn(inspector.getByLabel('Text after'), 'Esq.');
  await inspector.getByRole('switch', { name: 'Show a character counter' }).check();
  await axeInside(page, 'jfb-designer-inspector');

  // The number field: its limits, step and decimals, the texts around it.
  await design.getByRole('button', { name: /^Guests, / }).click();
  await expect(inspector.getByRole('heading', { name: 'Number field' })).toBeVisible();
  await openAll(inspector);
  await typeIn(inspector.getByLabel('Minimum', { exact: true }), '1');
  await typeIn(inspector.getByLabel('Maximum', { exact: true }), '12');
  await typeIn(inspector.getByLabel('Step', { exact: true }), '0.5');
  await typeIn(inspector.getByLabel('Decimal places'), '1');
  await typeIn(inspector.getByLabel('Text before', { exact: true }), 'about');
  await typeIn(inspector.getByLabel('Text after', { exact: true }), 'people');
  await axeInside(page, 'jfb-designer-inspector');

  // The date field: today by default, the earliest from today, the latest a date, weekdays only, dates left out.
  await design.getByRole('button', { name: /^Arrival, / }).click();
  await expect(inspector.getByRole('heading', { name: 'Date field' })).toBeVisible();
  await openAll(inspector);
  await inspector.getByRole('switch', { name: 'Start with today' }).check();
  const earliest = inspector.getByRole('group', { name: 'Earliest date' });
  await earliest.getByRole('combobox', { name: 'Kind of limit' }).selectOption('today');
  await typeIn(earliest.getByRole('spinbutton', { name: 'Number' }), '2');
  await earliest.getByRole('combobox', { name: 'Unit' }).selectOption('w');
  const latest = inspector.getByRole('group', { name: 'Latest date' });
  await latest.getByRole('combobox', { name: 'Kind of limit' }).selectOption('date');
  await latest.getByLabel('Date').fill('2026-12-31');
  await inspector.getByRole('switch', { name: 'Monday to Friday only' }).check();
  await typeIn(inspector.getByLabel('Excluded dates'), '2026-12-24\n2026-12-25');
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  const { fields } = await draftOf(api, key);
  expect(fields.get('name')?.settings).toEqual({
    minLength: 2,
    maxLength: 120,
    prefix: { '': 'Dr.' },
    suffix: { '': 'Esq.' },
    counter: true,
  });
  expect(fields.get('guests')?.settings).toEqual({
    min: '1',
    max: '12',
    step: '0.5',
    decimals: 1,
    prefix: { '': 'about' },
    suffix: { '': 'people' },
  });
  expect(fields.get('arrival')?.settings).toEqual({
    defaultToday: true,
    min: { today: '+2w' },
    max: '2026-12-31',
    weekdaysOnly: true,
    excluded: ['2026-12-24', '2026-12-25'],
  });

  // Ctrl+Z takes back the last change, with the date field it was made on selected again, and nothing before it.
  await design.getByRole('button', { name: /^Your name, / }).focus();
  await page.keyboard.press('Control+z');
  await expect(design.getByRole('button', { name: /^Arrival, / })).toHaveAttribute('aria-current', 'true');
  await expect(inspector.getByLabel('Excluded dates')).toHaveValue('');
  await expect(inspector.getByRole('switch', { name: 'Monday to Friday only' })).toBeChecked();
});

test('at a 1024 px window, selecting a field folds the palette to its rail, and clearing the selection opens it again', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Inspector fold'), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
  ]);
  await page.setViewportSize({ width: 1024, height: 768 });
  await loginAs('designer');
  const design = await open(page, key);
  const palette = design.locator('jfb-designer-palette');
  await expect(palette).toHaveAttribute('mode', 'docked');
  const name = design.getByRole('button', { name: /^Name, / });
  await name.click();
  await expect(palette).toHaveAttribute('mode', 'rail');
  await expect(design.locator('jfb-designer-inspector')).toHaveAttribute('mode', 'docked');
  // Its search opens it over the canvas, and Escape gives the focus back.
  await palette.getByRole('button', { name: 'Search fields' }).click();
  await expect(palette).toHaveAttribute('mode', 'overlay');
  await page.keyboard.press('Escape');
  await expect(palette).toHaveAttribute('mode', 'rail');
  await name.focus();
  await page.keyboard.press('Escape');
  await expect(design.locator('jfb-designer-inspector')).toHaveCount(0);
  await expect(palette).toHaveAttribute('mode', 'docked');
  await expect.poll(() => holdsFocus(name)).toBe(true);
});

test('a new page starts after a selected field from its "...", with the keyboard only', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Inspector new page'), [
    { type: 'text', alias: 'first', label: { '': 'First' } },
    { type: 'text', alias: 'second', label: { '': 'Second' } },
    { type: 'text', alias: 'third', label: { '': 'Third' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const steps = design.getByRole('navigation', { name: 'Pages' });
  await design.getByRole('button', { name: /^Second, / }).focus();
  await page.keyboard.press('Enter');
  await expect(design.getByRole('button', { name: /^Second, / })).toHaveAttribute('aria-current', 'true');
  const more = design.locator('jfb-canvas-field[key] .bo-tools').getByRole('button', { name: 'More actions' });
  for (let presses = 0; presses < 8 && !(await holdsFocus(more)); presses++) await page.keyboard.press('Tab');
  expect(await holdsFocus(more)).toBe(true);
  await page.keyboard.press('Enter');
  const item = design.getByRole('button', { name: 'Start a new page after this' });
  await expect.poll(() => holdsFocus(item)).toBe(true);
  await page.keyboard.press('Enter');
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  await expect(design.getByRole('button', { name: 'Page 2, page' })).toBeFocused();
  await expect(design.getByRole('button', { name: /^Third, / })).toBeVisible();
  await expect(design.getByRole('button', { name: /^Second, / })).toHaveCount(0);
  await expect(design.locator('[role="status"]')).toHaveText('Started a new page after Second');
  await saveDraft(page);
  const { pages } = await draftOf(api, key);
  expect(pages.map((each) => each.groups.flatMap((group) => group.fields.map((field) => field.alias)))).toEqual([
    ['first', 'second'],
    ['third'],
  ]);
});

test("on the site's two languages, a field's German label is edited in the inspector, and a field without German text is marked", async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Inspector German'), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'mail', label: { '': 'Email address', 'de-DE': 'E-Mail-Adresse' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const canvas = design.locator('jfb-form-canvas');
  await canvas.getByRole('combobox', { name: 'Language' }).selectOption('de-DE');
  await expect(canvas.locator('.bo-chip')).toHaveCount(1);
  await expect(canvas.locator('.bo-chip')).toHaveText('No German text yet');
  await design.getByRole('button', { name: /^Name, / }).click();
  const inspector = design.locator('jfb-designer-inspector');
  await expect(inspector.locator('.note')).toHaveText(
    'Editing the German texts. Where one is empty, visitors see the English text.',
  );
  const label = inspector.getByLabel('Label', { exact: true });
  await expect(label).toHaveValue('');
  await expect(label).toHaveAttribute('placeholder', 'Name');
  await label.fill('Ihr Name');
  await expect(design.getByRole('button', { name: /^Ihr Name, / })).toBeVisible();
  await design.getByRole('button', { name: /^E-Mail-Adresse, / }).click();
  await expect(design.getByRole('button', { name: /^Ihr Name, / })).toHaveAttribute('aria-current', 'false');
  await expect(canvas.locator('.bo-chip')).toHaveCount(0);
  await axeInside(page, 'jfb-form-design-view');
  await saveDraft(page);
  const { fields } = await draftOf(api, key);
  expect(fields.get('name')?.label).toEqual({ '': 'Name', 'de-DE': 'Ihr Name' });
});

test('an alias is checked as the form checks it, and a free one is kept', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Inspector alias'), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'mail', label: { '': 'Email' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  await design.getByRole('button', { name: /^Name, / }).click();
  const inspector = design.locator('jfb-designer-inspector');
  const advanced = inspector.getByRole('button', { name: /^Advanced/ });
  if ((await advanced.getAttribute('aria-expanded')) === 'false') await advanced.click();
  const alias = inspector.getByLabel('Alias', { exact: true });
  await typeIn(alias, 'mail');
  await expect(inspector.getByText('Another field has or had that alias.')).toBeVisible();
  await typeIn(alias, 'fullName');
  await expect(inspector.getByText('Another field has or had that alias.')).toHaveCount(0);
  await saveDraft(page);
  const { fields } = await draftOf(api, key);
  expect(fields.get('fullName')?.label).toEqual({ '': 'Name' });
  expect(fields.get('fullName')).not.toHaveProperty('formerAliases');
});
