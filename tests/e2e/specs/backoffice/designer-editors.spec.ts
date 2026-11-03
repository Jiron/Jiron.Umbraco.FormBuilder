import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// The inspector's richer editors (ROADMAP 9.4c, ADR-0173): a field's tag goes into a heading through "Insert merge
// tag", which offers only the answers before the heading; a consent's wording is written as one line of rich text, with
// the form's tag put in at the caret, bold words and a link; an image's picture is picked with Umbraco's media picker;
// a pattern is tried against answers in "Try it" before it is kept; and a dropdown takes its options from the pages of
// one type under one picked with Umbraco's content picker, which the canvas shows. Each is saved and read back.

const SECTION = '/umbraco/section/jfb-forms';
/** The demo's home page, whose children are the site's pages (demo/Seeding/DemoContent.json). */
const HOME = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';

interface DraftField {
  readonly key: string;
  readonly alias?: string;
  readonly settings?: Record<string, unknown>;
  readonly validation?: readonly Record<string, unknown>[];
  readonly options?: { readonly source?: string; readonly config?: Record<string, unknown> };
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

/** Whether an element holds the focus, followed into the shadow roots it lies in. */
const holdsFocus = (target: Locator) =>
  target.evaluate((element) => {
    let found = document.activeElement;
    while (found?.shadowRoot?.activeElement) found = found.shadowRoot.activeElement;
    return found === element;
  });

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

/** Umbraco's open picker, beside the inspector's own Choose. */
const modal = (page: Page) => page.locator('umb-backoffice-modal-container');

/** A field on the canvas by its alias's place: the fields are given in this order. */
const fieldAt = (design: Locator, at: number) => design.locator('jfb-canvas-field').nth(at);

test("a field's tag goes into a heading through the picker, and a consent's wording is written as rich text", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Tags and rich text'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' } },
    { type: 'heading', alias: 'welcome', settings: { text: { '': 'Welcome, ' } } },
    { type: 'consent', alias: 'agree', label: { '': 'Consent' } },
    { type: 'email', alias: 'email', label: { '': 'Email address' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  await fieldAt(design, 1).click();
  const heading = inspector.getByRole('textbox', { name: 'Heading', exact: true });
  await heading.click();
  await heading.press('End');
  await inspector.getByRole('button', { name: 'Insert merge tag' }).click();
  const tags = page.getByRole('group', { name: 'Insert merge tag' });
  // The heading recalls the answers before it: the name, not the email address after it.
  await expect(tags.getByRole('list', { name: 'Answers' }).getByRole('button')).toHaveText([
    'Your name {{ field.name }}',
  ]);
  await axeInside(page, 'jfb-designer-inspector');
  await tags.getByRole('searchbox', { name: 'Find a merge tag' }).fill('your');
  await expect(tags.getByRole('button')).toHaveCount(1);
  await tags.getByRole('button', { name: 'Your name {{ field.name }}' }).click();
  await expect(tags).toBeHidden();
  await expect(heading).toHaveValue('Welcome, {{ field.name }}');
  expect(await holdsFocus(heading)).toBe(true);

  await fieldAt(design, 2).click();
  const wording = inspector.getByRole('textbox', { name: 'Consent text' });
  await wording.click();
  await page.keyboard.type('For ');
  await inspector.getByRole('button', { name: 'Insert merge tag' }).click();
  await tags.getByRole('button', { name: 'Name of the form {{ form.name }}' }).click();
  await expect(tags).toBeHidden();
  await page.keyboard.type(', I agree to the ');
  await inspector.getByRole('button', { name: 'Bold' }).click();
  await expect(inspector.getByRole('button', { name: 'Bold' })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.type('privacy terms');
  await page.keyboard.press('Control+b');
  await page.keyboard.type('.');
  await page.keyboard.press('ArrowLeft');
  for (let i = 0; i < 'privacy terms'.length; i++) await page.keyboard.press('Shift+ArrowLeft');
  await inspector.getByRole('button', { name: 'Link' }).click();
  const address = inspector.getByRole('textbox', { name: 'Address' });
  await address.fill('privacy');
  await inspector.getByRole('button', { name: 'Apply' }).click();
  await expect(address).toHaveAttribute('aria-invalid', 'true');
  await address.fill('/privacy');
  await inspector.getByRole('button', { name: 'Apply' }).click();
  await expect(address).toBeHidden();
  // The canvas draws a draft's rich text as bare tags, a link without its address (ADR-0168).
  await expect(fieldAt(design, 2).locator('label a strong')).toHaveText('privacy terms');
  // A link in the text is edited, never followed out of the form.
  await wording.locator('a').click();
  await expect(page).toHaveURL(new RegExp(`/edit/${key}$`));
  // One line of words in the label: Enter starts no paragraph.
  await page.keyboard.press('End');
  await page.keyboard.press('Enter');
  await expect(fieldAt(design, 2).locator('label p')).toHaveCount(0);
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  const fields = await fieldsOf(api, key);
  expect(fields.get('welcome')?.settings).toEqual({ text: { '': 'Welcome, {{ field.name }}' } });
  expect(fields.get('agree')?.settings?.['text']).toEqual({
    '': 'For {{ form.name }}, I agree to the <a href="/privacy"><strong>privacy terms</strong></a>.',
  });
});

test("an image's picture is picked with Umbraco's media picker, which the canvas shows", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('A picture'), [{ type: 'image', alias: 'picture' }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  await fieldAt(design, 0).click();
  await inspector.locator('umb-input-media').getByRole('button', { name: 'Choose' }).click();
  // Umbraco's card animates without end, so it is never "stable" for an ordinary click; the picker slides in first.
  const picture = page.getByRole('img', { name: 'Sample picture' });
  await expect(picture).toBeInViewport({ ratio: 1 });
  await picture.click({ force: true });
  await modal(page).getByRole('button', { name: 'Choose', exact: true }).click();
  await expect(inspector.locator('umb-input-media').getByText('Sample picture')).toBeVisible();
  await expect(fieldAt(design, 0).locator('img')).toHaveCount(1);
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  expect((await fieldsOf(api, key)).get('picture')?.settings?.['media']).toMatch(/^[0-9a-f-]{36}$/);
});

test('a pattern is tried against answers before it is kept, with its own message', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('A pattern'), [
    { type: 'text', alias: 'code', label: { '': 'Member code' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  await fieldAt(design, 0).click();
  await inspector.getByRole('button', { name: /^Validation/ }).click();
  const pattern = inspector.getByRole('textbox', { name: 'Must match a pattern' });
  await pattern.fill('(?=M)M');
  await expect(pattern).toHaveAttribute('aria-invalid', 'true');
  await expect(inspector.getByText("The site's matcher doesn't take this pattern")).toBeVisible();
  await pattern.fill('M-[0-9]{4}');
  await expect(pattern).toHaveAttribute('aria-invalid', 'false');
  await expect(pattern).toHaveCSS('font-family', /monospace/);
  const sample = inspector.getByRole('textbox', { name: 'Try it' });
  const result = inspector.getByRole('status').filter({ hasText: 'pattern' });
  await sample.fill('M-12');
  await expect(result).toHaveText("The pattern doesn't take this answer.");
  await sample.fill('M-1234');
  await expect(result).toHaveText('The pattern takes this answer.');
  await inspector.getByRole('textbox', { name: 'Message when it fails' }).fill('Your code is M- and four digits.');
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  expect((await fieldsOf(api, key)).get('code')?.validation).toEqual([
    { kind: 'pattern', pattern: 'M-[0-9]{4}', message: { '': 'Your code is M- and four digits.' } },
  ]);
});

test("a dropdown takes its options from the pages of one type under one picked with Umbraco's content picker", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Pages as options'), [
    { type: 'dropdown', alias: 'topic', label: { '': 'Topic' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  const design = await open(page, key);
  const inspector = design.locator('jfb-designer-inspector');

  await fieldAt(design, 0).click();
  await inspector.getByLabel('Options from').selectOption('content');
  await inspector.locator('umb-input-document').getByRole('button', { name: 'Choose' }).click();
  await modal(page).getByRole('button', { name: 'Home', exact: true }).click({ force: true });
  await modal(page).getByRole('button', { name: 'Choose', exact: true }).click();
  await expect(inspector.locator('umb-input-document').getByText('Home')).toBeVisible();
  const options = fieldAt(design, 0).locator('select option:not([value=""])');
  await expect(options.first()).toHaveText('Events');
  await expect(options).toContainText(['Events', 'Careers', 'Contact', 'Privacy']);
  // The site's page types are ticked without Umbraco's Settings section, which designers don't have.
  const types = inspector.getByRole('group', { name: 'Document types' });
  await types.getByRole('checkbox', { name: 'Demo legal page' }).check();
  await expect(options).toHaveText(['Privacy', 'Imprint']);
  await axeInside(page, 'jfb-designer-inspector');

  await saveDraft(page);
  const config = (await fieldsOf(api, key)).get('topic')?.options?.config;
  expect(config?.['root']).toBe(HOME);
  expect(config?.['documentTypes']).toEqual([expect.stringMatching(/^[0-9a-f-]{36}$/)]);
});
