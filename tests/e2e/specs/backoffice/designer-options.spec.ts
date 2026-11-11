import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// The option editor (ROADMAP 9.4b, ADR-0172): a dropdown takes its options from the library's list the demo seeds and
// another from a standard list, depending on the first, and the canvas shows the options they resolve in English and
// German; a radio field's options are pasted one a line, moved by drag, with Alt+Up and with single presses (its
// handle's "Position in the list", WCAG 2.5.7, ROADMAP 10.4), and one gets a place limit; a grid's rows and columns are
// added, pasted, marked, moved and removed. Each is saved and read back from the draft.

const SECTION = '/umbraco/section/jfb-forms';

/** Whether an element holds the focus, followed into the shadow roots it lies in. */
const holdsFocus = (target: Locator) =>
  target.evaluate((element) => {
    let found = document.activeElement;
    while (found?.shadowRoot?.activeElement) found = found.shadowRoot.activeElement;
    return found === element;
  });

interface DraftOption {
  readonly value: string;
  readonly label?: Record<string, string>;
  readonly places?: number;
}

interface DraftField {
  readonly key: string;
  readonly alias?: string;
  readonly options?: {
    readonly source?: string;
    readonly list?: string;
    readonly config?: Record<string, unknown>;
    readonly parent?: string;
    readonly items?: readonly DraftOption[];
  };
  readonly rows?: readonly {
    readonly key: string;
    readonly label?: Record<string, string>;
    readonly required?: boolean;
  }[];
}

/** The fields of a form's saved draft, by alias. */
async function fieldsOf(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: DraftField[] }[] }[] };
  };
  const fields = new Map<string, DraftField>();
  for (const page of draft.definition.pages)
    for (const group of page.groups) for (const field of group.fields) fields.set(field.alias ?? field.key, field);
  return fields;
}

async function open(page: Page, key: string) {
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

/** The labels of the own options or rows in the inspector, in their order. */
const labelsIn = (editor: Locator) =>
  editor
    .locator('.option-rows > .option-row .row-label')
    .evaluateAll((boxes) => boxes.map((box) => (box as HTMLInputElement).value));

/** The options a field's dropdown shows on the canvas, without its prompt. */
const canvasOptions = (design: Locator, label: string) =>
  design
    .locator('jfb-canvas-field')
    .filter({ has: design.page().getByRole('button', { name: new RegExp(`^${label}, `) }) })
    .locator('select option:not([value=""])');

test("a dropdown takes the library's list, another a standard list depending on it, and the canvas shows what they resolve", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Options from elsewhere'), [
    { type: 'dropdown', alias: 'branch', label: { '': 'Branch', 'de-DE': 'Zweigstelle' } },
    { type: 'dropdown', alias: 'month', label: { '': 'Month', 'de-DE': 'Monat' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  await design.getByRole('button', { name: /^Branch, / }).click();
  await inspector.getByLabel('Options from').selectOption('list');
  await inspector.getByLabel('Option list').selectOption({ label: 'Library branches \u00b7 3 options' });
  await expect(canvasOptions(design, 'Branch')).toHaveText(['Central library', 'Harbour branch', 'Hillside branch']);
  await axeInside(page, 'jfb-designer-inspector');

  await design.getByRole('button', { name: /^Month, / }).click();
  await inspector.getByLabel('Options from').selectOption('code');
  await inspector.getByLabel('Standard list').selectOption({ label: 'Months' });
  await expect(canvasOptions(design, 'Month')).toHaveCount(12);
  await expect(canvasOptions(design, 'Month').first()).toHaveText('January');
  await inspector.getByLabel('Depends on').selectOption({ label: 'Branch' });
  // A cascading field shows every option it may offer on the canvas.
  await expect(canvasOptions(design, 'Month')).toHaveCount(12);
  await axeInside(page, 'jfb-designer-inspector');

  // In German, the canvas asks for the options in German.
  await design.locator('jfb-form-canvas').getByRole('combobox', { name: 'Language' }).selectOption('de-DE');
  await expect(canvasOptions(design, 'Zweigstelle').first()).toHaveText('Zentralbibliothek');
  await expect(canvasOptions(design, 'Monat').first()).toHaveText('Januar');

  await saveDraft(page);
  const fields = await fieldsOf(api, key);
  const branch = fields.get('branch');
  expect(branch?.options?.source).toBe('list');
  expect(branch?.options?.list).toMatch(/^[0-9a-f-]{36}$/);
  expect(fields.get('month')?.options).toEqual({ source: 'code', config: { source: 'months' }, parent: branch?.key });
});

test("a radio field's options are pasted one a line, moved by drag, with Alt+Up and with single presses, and one gets a place limit", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Options pasted'), [
    { type: 'radio', alias: 'session', label: { '': 'Session' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');
  const editor = inspector.locator('jfb-option-editor');
  const status = editor.getByRole('status');

  await design.getByRole('button', { name: /^Session, / }).click();
  await expect(editor.getByText('No options yet. Add them one by one, or paste a list.')).toBeVisible();
  await editor.getByRole('button', { name: 'Paste options\u2026' }).click();
  const lines = editor.getByLabel('One option a line');
  await expect.poll(() => holdsFocus(lines)).toBe(true);
  await lines.fill(['Morning', 'Afternoon', 'Evening', 'morning'].join('\n'));
  await editor.getByRole('button', { name: 'Add these options' }).click();
  await expect(status).toHaveText('Added 3 options. 1 line was an option already.');
  expect(await labelsIn(editor)).toEqual(['Morning', 'Afternoon', 'Evening']);
  await expect(design.locator('jfb-canvas-field').filter({ hasText: 'Session' }).getByRole('radio')).toHaveCount(3);

  // Alt+Up on Evening's handle moves it before Afternoon, says where it went and keeps the focus on it.
  const evening = editor.getByRole('button', { name: 'Move Evening', exact: true });
  await evening.focus();
  await page.keyboard.press('Alt+ArrowUp');
  await expect(status).toHaveText('Moved. Evening: position 2 of 3.');
  expect(await labelsIn(editor)).toEqual(['Morning', 'Evening', 'Afternoon']);
  await expect.poll(() => holdsFocus(evening)).toBe(true);
  // The selected field stays where it is: the row took the keys.
  await expect(design.getByRole('button', { name: /^Session, / })).toHaveAttribute('aria-current', 'true');

  // Morning's handle, dragged below Afternoon, puts it last.
  const handle = (await editor.getByRole('button', { name: 'Move Morning', exact: true }).boundingBox())!;
  const afternoon = (await editor.getByRole('button', { name: 'Move Afternoon', exact: true }).boundingBox())!;
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2, afternoon.y + afternoon.height + 4, { steps: 10 });
  await expect(editor.locator('.option-row[data-line-after]')).toHaveCount(1);
  await page.mouse.up();
  await expect(status).toHaveText('Moved. Morning: position 3 of 3.');
  expect(await labelsIn(editor)).toEqual(['Evening', 'Afternoon', 'Morning']);

  // With single presses, no drag: a press on Evening's handle opens its place in the list, and 3 of 3 puts it last.
  const moveEvening = editor.getByRole('button', { name: 'Move Evening', exact: true });
  await moveEvening.click();
  await expect(moveEvening).toHaveAttribute('aria-expanded', 'true');
  const place = editor.getByLabel('Position in the list');
  await expect(place).toHaveValue('0');
  await expect.poll(() => holdsFocus(place)).toBe(true);
  await place.selectOption({ label: '3 of 3' });
  await expect(status).toHaveText('Moved. Evening: position 3 of 3.');
  expect(await labelsIn(editor)).toEqual(['Afternoon', 'Morning', 'Evening']);
  await expect(editor.getByLabel('Position in the list')).toHaveCount(0);
  await expect.poll(() => holdsFocus(moveEvening)).toBe(true);

  // Afternoon's details: twelve places, and a stored value that is refused before one that is kept.
  await editor.getByRole('button', { name: 'Details of Afternoon' }).click();
  const places = editor.getByLabel('Places');
  await places.fill('twelve');
  await places.press('Tab');
  await expect(editor.getByText('Enter a whole number from 0 to 1,000,000, or nothing for no limit.')).toBeVisible();
  // The box is marked as well as described.
  await expect(places).toHaveAttribute('aria-invalid', 'true');
  await expect(places).toHaveCSS('border-top-width', '2px');
  await places.fill('12');
  await places.press('Tab');
  await expect(places).toHaveAttribute('aria-invalid', 'false');
  const stored = editor.getByLabel('Stored as');
  await stored.fill('evening');
  await stored.press('Tab');
  await expect(editor.getByText('Another option stores this already.')).toBeVisible();
  await stored.fill('pm');
  await stored.press('Tab');
  await expect(editor.getByText('Another option stores this already.')).toHaveCount(0);
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  const fields = await fieldsOf(api, key);
  // A pasted option stores its label as visitors' answers store it; Afternoon's was changed.
  expect(fields.get('session')?.options?.items).toEqual([
    { value: 'pm', label: { '': 'Afternoon' }, places: 12 },
    { value: 'Morning', label: { '': 'Morning' } },
    { value: 'Evening', label: { '': 'Evening' } },
  ]);
});

test("an option on the form's cards gets a description and an image from the media library, which the canvas shows", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Options cards'), [
    {
      type: 'radio',
      alias: 'room',
      label: { '': 'Room' },
      options: { items: [{ value: 'hall', label: { '': 'Hall' } }] },
    },
  ]);
  // The field inherits the form's layout of choices, which is cards.
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown>;
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: { ...draft.definition, settings: { appearance: { choiceLayout: 'cards' } } },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const editor = design.locator('jfb-designer-inspector jfb-option-editor');
  await design.getByRole('button', { name: /^Room, / }).click();
  await editor.getByRole('button', { name: 'Details of Hall' }).click();
  await editor.getByLabel('Description').fill('Seats 200');
  await editor.getByRole('button', { name: 'Choose image\u2026' }).click();
  // Umbraco's media picker, with the demo's one picture.
  // Umbraco's card animates without end, so it is never "stable" for an ordinary click; the picker slides in first.
  const picture = page.getByRole('img', { name: 'Sample picture' });
  await expect(picture).toBeInViewport({ ratio: 1 });
  await picture.click({ force: true });
  await page.getByRole('button', { name: 'Choose', exact: true }).click();
  await expect(editor.getByText('An image is chosen.')).toBeVisible();
  const card = design.locator('jfb-canvas-field').filter({ hasText: 'Room' });
  await expect(card.getByText('Seats 200')).toBeVisible();
  await expect(card.locator('img')).toHaveCount(1);
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  expect((await fieldsOf(api, key)).get('room')?.options?.items).toEqual([
    {
      value: 'hall',
      label: { '': 'Hall' },
      description: { '': 'Seats 200' },
      image: 'd0000000-0000-4000-8000-0000000000f1',
    },
  ]);
});

test("a grid's rows and columns are added, pasted, marked, moved and removed", async ({ page, loginAs, apiAs }) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Options grid'), [
    {
      type: 'grid',
      alias: 'rating',
      label: { '': 'Rate the day' },
      options: { items: [{ value: 'poor', label: { '': 'Poor' } }] },
    },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');
  await design.getByRole('button', { name: /^Rate the day, / }).click();
  const [rows, columns] = [
    inspector.locator('jfb-option-editor').nth(0),
    inspector.locator('jfb-option-editor').nth(1),
  ];
  await expect(rows.getByText('No rows yet. Each row is a statement visitors answer on the columns.')).toBeVisible();

  // A row typed, two pasted, the first needing an answer, the last moved up, one removed.
  await rows.getByRole('button', { name: 'Add row' }).click();
  await expect.poll(() => holdsFocus(rows.getByLabel('Statement of row 1'))).toBe(true);
  await page.keyboard.type('The talks');
  await rows.getByRole('button', { name: 'Paste rows\u2026' }).click();
  await rows.getByLabel('One statement a line').fill(['The rooms', 'The food'].join('\n'));
  await rows.getByRole('button', { name: 'Add these rows' }).click();
  await expect(rows.getByRole('status')).toHaveText('Added 2 rows.');
  expect(await labelsIn(rows)).toEqual(['The talks', 'The rooms', 'The food']);
  await rows.locator('.option-row').nth(0).getByRole('checkbox', { name: 'Needs an answer' }).check();
  await rows.getByRole('button', { name: 'Move The food', exact: true }).focus();
  await page.keyboard.press('Alt+ArrowUp');
  await expect(rows.getByRole('status')).toHaveText('Moved. The food: position 2 of 3.');
  await rows.getByRole('button', { name: 'Remove The rooms' }).click();
  expect(await labelsIn(rows)).toEqual(['The talks', 'The food']);

  // The columns, in a grid's own words: where they come from, one added, one renamed, the first removed.
  await expect(inspector.getByLabel('Columns from')).toBeVisible();
  await columns.getByRole('button', { name: 'Add column' }).click();
  await page.keyboard.type('Good');
  await columns.getByRole('button', { name: 'Remove Poor' }).click();
  expect(await labelsIn(columns)).toEqual(['Good']);
  const grid = design.locator('jfb-canvas-field').filter({ hasText: 'Rate the day' });
  await expect(grid.getByText('The food')).toBeVisible();
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  const rating = (await fieldsOf(api, key)).get('rating');
  expect(rating?.rows?.map((row) => [row.label, row.required ?? false])).toEqual([
    [{ '': 'The talks' }, true],
    [{ '': 'The food' }, false],
  ]);
  expect(rating?.options?.items?.map((item) => item.label)).toEqual([{ '': 'Good' }]);
});
