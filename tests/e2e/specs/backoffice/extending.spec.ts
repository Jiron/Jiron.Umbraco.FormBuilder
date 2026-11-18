import type { APIRequestContext, Locator } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { openSections } from '../../support/form-views.ts';
import { addFieldOf, openPicker, pickerClosed, pickerItem } from '../../support/picker.ts';

// A site's own extensions of the designer (ROADMAP 15.1, ADR-0233), from the samples this host turns on (ADR-0234): the
// colour sample's field type draws its colours with the site's own setting editor, under the setting's label in the
// inspector, and the site's own preview draws the field on the canvas, following every change; what the editor gives
// is the setting's value the draft keeps. The embed sample's view stands beside the form's own views and reads the
// open form from the workspace's context.

const SECTION = '/umbraco/section/jfb-forms';

interface Swatch {
  readonly name: string;
  readonly value: string;
}

/** The colours setting of the form's first field, as its saved draft holds it. */
async function savedColours(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: { settings?: { colours?: Swatch[] } }[] }[] }[] };
  };
  return draft.definition.pages[0]?.groups[0]?.fields[0]?.settings?.colours;
}

/** The names the canvas's preview shows, in order. */
const shownNames = (preview: Locator) => preview.locator('li').allInnerTexts();

test("a site's setting editor draws the colours under their label, and its preview follows them on the canvas", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Site colours'), [
    { type: 'sample.colour', alias: 'favourite', label: { '': 'Favourite colour' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  const field = design.locator('jfb-canvas-field').first();

  // The canvas draws the field with the site's preview: its label, then the type's default colours.
  const preview = field.locator('sample-colour-preview');
  await expect(preview.locator('li')).toHaveCount(3);
  expect(await shownNames(preview)).toEqual(['Blue', 'Green', 'Red']);
  await expect(field).toContainText('Favourite colour');

  // The inspector draws the colours with the site's editor, under the setting's label in the backoffice's words.
  await openPicker(page, addFieldOf(design));
  await expect(pickerItem(page, 'Colour')).toBeVisible();
  await page.keyboard.press('Escape');
  await pickerClosed(page);
  await design.getByRole('button', { name: 'Favourite colour, colour field' }).click();
  const inspector = design.locator('jfb-designer-inspector');
  await expect(inspector.getByRole('heading', { name: 'Colour field' })).toBeVisible();
  const editor = inspector.locator('sample-swatches-editor');
  const colours = editor.getByRole('list', { name: 'Colours' });
  await expect(colours).toBeVisible();
  await expect(inspector.getByText('Colours', { exact: true })).toBeVisible();
  await expect(colours.getByRole('listitem')).toHaveCount(3);
  await expect(editor.getByRole('textbox', { name: 'Name of colour 2' })).toHaveValue('Green');

  // A colour renamed, one added with its own value and name, and one removed: the canvas follows each.
  const second = editor.getByRole('textbox', { name: 'Name of colour 2' });
  await second.fill('Forest');
  await second.press('Tab');
  await expect.poll(() => shownNames(preview)).toEqual(['Blue', 'Forest', 'Red']);

  await editor.getByRole('button', { name: 'Add a colour' }).click();
  const added = editor.getByRole('textbox', { name: 'Name of colour 4' });
  await expect(added).toBeFocused();
  await expect(added).toHaveValue('New colour');
  await added.fill('Sun');
  await added.press('Tab');
  await editor.getByLabel('Colour of Sun').fill('#ffd33d');
  await expect.poll(() => shownNames(preview)).toEqual(['Blue', 'Forest', 'Red', 'Sun']);
  await expect(preview.locator('li .swatch').last()).toHaveCSS('background-color', 'rgb(255, 211, 61)');

  await editor.getByRole('button', { name: 'Remove Blue' }).click();
  await expect(colours.getByRole('listitem')).toHaveCount(3);
  // The focus goes to what took the removed colour's place.
  await expect(editor.getByRole('button', { name: 'Remove Forest' })).toBeFocused();
  await expect.poll(() => shownNames(preview)).toEqual(['Forest', 'Red', 'Sun']);

  // The built-in switch beside it hides the names, which stay the swatches' text for a screen reader.
  await openSections(inspector);
  await inspector.getByRole('switch', { name: "Show the colours' names" }).uncheck();
  await expect(preview.locator('.name').first()).toHaveClass(/hidden/);

  await axeInside(page, 'jfb-designer-inspector');
  await axeInside(page, 'jfb-form-canvas');

  // What the editor gave is the setting's value the draft keeps.
  const saved = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok()).toBe(true);
  expect(await savedColours(api, key)).toEqual([
    { name: 'Forest', value: '#2da44e' },
    { name: 'Red', value: '#cf222e' },
    { name: 'Sun', value: '#ffd33d' },
  ]);
});

test("a site's workspace view stands beside the form's own views and reads the open form", async ({
  page,
  loginAs,
  apiAs,
  baseURL,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Embedded'), [{ type: 'text', alias: 'name' }]);
  const { alias } = (await (await api.get(`${API}/form/${key}`)).json()) as { alias: string };
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: baseURL ?? '' });
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);

  // After the form's own views, under its label in the backoffice's words.
  const tabs = page.locator('umb-workspace-editor').getByRole('tab');
  const embed = page.locator('umb-workspace-editor').getByRole('tab', { name: 'Embed' });
  await expect(embed).toBeVisible();
  expect(await tabs.last().getAttribute('href')).toBe(await embed.getAttribute('href'));
  await embed.click();
  await expect(page).toHaveURL(/\/view\/embed$/);

  const view = page.locator('sample-embed-view');
  await expect(view.getByRole('heading', { name: 'In a Razor view' })).toBeVisible();
  const razor = view.locator('pre').first();
  await expect(razor).toHaveText(`<jfb-render form="${alias}" />`);
  await expect(view.locator('pre').last()).toContainText(`<jfb-form api="${baseURL}" form="${alias}"></jfb-form>`);

  await view.getByRole('button', { name: 'Copy: In a Razor view' }).click();
  await expect(view.getByRole('status')).toHaveText('Copied');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(`<jfb-render form="${alias}" />`);
  await axeInside(page, 'sample-embed-view');
});
