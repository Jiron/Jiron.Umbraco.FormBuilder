import { expect, test } from '../../fixtures.ts';
import { inLanguage } from '../../support/backoffice-look.ts';
import { formWithFields, unique } from '../../support/forms-api.ts';

// The selected element's bar (ROADMAP 9.6a, ADR-0182, carried in from 9.4c): the canvas keeps room above each element
// for its controls, so the bar of a selected field never lies over the element above it. A heading straight above a
// selected field stays in sight, whole, and a click on its words selects the heading, in English and with the longer
// German words alike.

for (const language of ['en', 'de'] as const) {
  test(`the bar of a selected field leaves the heading above it in sight and clickable (${language})`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const heading = crypto.randomUUID();
    const name = crypto.randomUUID();
    const key = await formWithFields(await apiAs('designer'), unique('Bar'), [
      { key: heading, type: 'heading', settings: { level: 3, text: { '': 'About your visit' } } },
      { key: name, type: 'text', alias: 'name', label: { '': 'Name' } },
    ]);
    if (language === 'de') await inLanguage(page, 'de-DE');
    await page.setViewportSize({ width: 1440, height: 900 });
    await loginAs('designer');
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`);
    const design = page.locator('jfb-form-design-view');
    const selectOf = (field: string) =>
      design.locator(`jfb-canvas-field[key="${field}"] [data-control="select"]`).first();

    await selectOf(name).click();
    await expect(selectOf(name)).toHaveAttribute('aria-current', 'true');
    const bar = design.locator(`jfb-canvas-field[key="${name}"] .bo-tools`).first();
    await expect(bar).toBeVisible();
    const words = design.locator(`jfb-canvas-field[key="${heading}"] .jfb-heading`).first();
    const [barBox, wordsBox] = [await bar.boundingBox(), await words.boundingBox()];
    expect(barBox && wordsBox, 'the bar and the heading are drawn').toBeTruthy();
    if (!barBox || !wordsBox) return;
    // The bar's top lies below the heading's bottom: nothing of the heading is under it.
    expect(barBox.y, "the bar's top").toBeGreaterThanOrEqual(wordsBox.y + wordsBox.height);

    // The heading's words take the click that selects it.
    await page.mouse.click(wordsBox.x + Math.min(wordsBox.width - 4, 120), wordsBox.y + wordsBox.height / 2);
    await expect(selectOf(heading)).toHaveAttribute('aria-current', 'true');
  });
}
