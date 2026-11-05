import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { inTheme } from '../../support/backoffice-look.ts';
import { API, blankForm, contactForm, created, formOf, publishWithLabel, unique } from '../../support/forms-api.ts';
import { splitAxe } from '../../support/ux-walk.ts';

// The section in Umbraco's dark theme (ROADMAP 9.6a, ADR-0182). The theme's filled buttons put their label in a grey
// that meets 4.3:1 on their fill; the section's elements give those labels white, as Umbraco's light and high-contrast
// themes do, and their own controls use colours that keep 4.5:1. Axe finds nothing inside the section's own elements
// with every kind of filled button in sight: a folder's, the Design view's toolbar, selected element and footer, the
// undo toast, the publish dialog's, the duplicate dialog's, the copied actions' links and the recycle bin's. The forms
// tree's rows are Umbraco's tree, whose open row is Umbraco's colours; the label we put in it takes them.

const SECTION = '/umbraco/section/jfb-forms';

/** Axe's WCAG 2.2 A and AA findings inside the section's own elements; Umbraco's own, its tree's rows among them, are
 * Umbraco's. */
async function ours(page: Page): Promise<string[]> {
  await page.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'));
  const result = await new AxeBuilder({ page })
    // The options first: options() replaces what withTags() sets.
    .options({ ancestry: true })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  return splitAxe(
    result.violations.flatMap((violation) =>
      violation.nodes.map((node) => ({
        rule: violation.id,
        target: node.target,
        ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
      })),
    ),
  ).ours.filter((finding) => !finding.includes('umb-section-sidebar'));
}

async function trash(api: APIRequestContext, key: string): Promise<void> {
  const { concurrencyVersion } = await formOf(api, key);
  expect((await api.post(`${API}/form/${key}/trash`, { data: { concurrencyVersion } })).ok()).toBe(true);
}

test("in the dark theme, axe finds nothing in the section's own elements, its filled buttons among them", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('admin');
  const folderName = unique('Dark folder');
  const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);
  await contactForm(api, unique('Dark in a folder'), folder);
  // At the root, so its row is in the tree while it is open: Umbraco opens no folder for a form shown by its address.
  const name = unique('Dark enquiries');
  const key = await contactForm(api, name);
  await trash(api, await contactForm(api, unique('Dark trashed')));
  // A form whose two emails name fields the blank target lacks, so the copy's result lists a link to fix each.
  const found = (await (await api.get(`${API}/form/search`, { params: { text: 'contact', take: 50 } })).json()) as {
    items: { key: string; alias?: string | null }[];
  };
  const contact = found.items.find((item) => item.alias === 'contact')?.key;
  const source = unique('Dark with actions');
  await created(
    await api.post(`${API}/form/${contact}/duplicate`, { data: { name: source, includeActions: true } }),
    source,
  );
  const target = unique('Dark target');
  await blankForm(api, target);
  await inTheme(page, 'dark');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');

  // A folder, with its Choose a template... and Create form buttons.
  await page.goto(`${SECTION}/workspace/jfb-folder/edit/${folder}`);
  await expect(page.locator('jfb-folder-list table')).toBeVisible();
  expect(await ours(page), 'a folder').toEqual([]);

  // The Design view: its toolbar, a selected field's bar, and the footer's Save draft and Publish....
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.locator('jfb-canvas-field [data-control="select"]').first().click();
  await expect(design.locator('jfb-designer-inspector h2')).toBeVisible();
  expect(await ours(page), 'the Design view').toEqual([]);

  // The open form's row in the tree: unpublished, its label takes the open row's own colour, not the muted one.
  const label = page.locator(`umb-section-sidebar uui-menu-item[label="${name}"] > .unpublished`).first();
  const colours = await label.evaluate((span) => [
    getComputedStyle(span).color,
    getComputedStyle(span.parentElement as Element).color,
  ]);
  expect(colours[0], "the open row's label").toBe(colours[1]);

  // Delete, with its Undo toast in Umbraco's corner.
  await design.locator('jfb-canvas-field .bo-tools button').filter({ visible: true }).nth(2).click();
  await expect(page.locator('jfb-undo-notification')).toBeVisible();
  expect(await ours(page), 'the undo toast').toEqual([]);

  // The publish dialog.
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  const publish = page.locator('jfb-publish-modal');
  await expect(publish.locator('.said')).not.toBeEmpty();
  expect(await ours(page), 'the publish dialog').toEqual([]);
  await publish.getByRole('button', { name: 'Cancel' }).click();

  // The duplicate dialog, from the form's "..." in the tree.
  const row = page.locator(`umb-section-sidebar uui-menu-item[label="${name}"]`).filter({ visible: true }).first();
  await row.hover();
  await page.getByRole('button', { name: `View actions for '${name}'`, exact: true }).click();
  await page.locator('umb-entity-action-list uui-menu-item[label="Duplicate\u2026"]').filter({ visible: true }).click();
  await expect(page.locator('jfb-duplicate-modal input').first()).toBeVisible();
  expect(await ours(page), 'the duplicate dialog').toEqual([]);
  await page.keyboard.press('Escape');

  // The copied actions' result, with a link to fix each.
  const from = page.locator(`umb-section-sidebar uui-menu-item[label="${source}"]`).filter({ visible: true }).first();
  await from.hover();
  await page.getByRole('button', { name: `View actions for '${source}'`, exact: true }).click();
  await page
    .locator('umb-entity-action-list uui-menu-item[label="Copy actions to\u2026"]')
    .filter({ visible: true })
    .click();
  const copying = page.locator('jfb-copy-actions-modal');
  await copying.getByRole('button', { name: 'Choose a form\u2026' }).click();
  const picker = page.locator('umb-tree-picker-modal');
  await picker.locator(`uui-menu-item[label="${target}"]`).click();
  await picker.getByRole('button', { name: 'Choose' }).click();
  await copying.getByRole('button', { name: 'Copy 2 actions' }).click();
  await expect(copying.locator('a').first()).toBeVisible();
  expect(await ours(page), "the copied actions' result").toEqual([]);
  await page.keyboard.press('Escape');

  // The recycle bin, with its Empty recycle bin....
  await page.goto(`${SECTION}/workspace/jfb-recycle-bin`);
  await expect(page.locator('jfb-recycle-bin table')).toBeVisible();
  expect(await ours(page), 'the recycle bin').toEqual([]);
});

/** An sRGB colour as getComputedStyle gives it, `rgb()` or `color(srgb ...)`, as 0-255 channels. */
const channels = (colour: string): number[] => {
  const srgb = /^color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/.exec(colour);
  if (srgb) return srgb.slice(1, 4).map((part) => Math.round(Number(part) * 255));
  const rgb = /^rgba?\((\d+),? (\d+),? (\d+)/.exec(colour);
  if (rgb) return rgb.slice(1, 4).map(Number);
  throw new Error(`not an sRGB colour: ${colour}`);
};

/** WCAG's contrast ratio of two colours. */
function contrast(one: string, other: string): number {
  const luminance = (colour: string) => {
    const [r, g, b] = channels(colour).map((value) => {
      const c = value / 255;
      return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * (r ?? 0) + 0.7152 * (g ?? 0) + 0.0722 * (b ?? 0);
  };
  const [light, dark] = [luminance(one), luminance(other)].sort((a, b) => b - a);
  return ((light ?? 0) + 0.05) / ((dark ?? 0) + 0.05);
}

/** The focused element at the bottom of the shadow roots, and the colour of its ring. */
const ring = (page: Page) =>
  page.evaluate(() => {
    let active = document.activeElement;
    while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
    const style = active ? getComputedStyle(active) : undefined;
    return { tag: active?.localName ?? '', style: style?.outlineStyle ?? '', colour: style?.outlineColor ?? '' };
  });

test("in the dark theme, the section's focus rings and status colours keep 3:1, and a warning's label 4.5:1", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const key = await contactForm(api, unique('Dark rings'));
  // Published, then changed: the state chip says it has changes to publish, in the warning's colours.
  await publishWithLabel(api, key, 'Your name');
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { label: Record<string, string> }[] }[] }[] };
  };
  const field = draft.definition.pages[0]?.groups[0]?.fields[0];
  if (field) field.label = { '': 'Full name' };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await inTheme(page, 'dark');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.locator('jfb-canvas-field [data-control="select"]').first().click();
  await expect(design.locator('jfb-designer-inspector h2')).toBeVisible();
  const surface = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--uui-color-surface').trim(),
  );
  const surfaceColour = await page.evaluate((value) => {
    const probe = document.createElement('i');
    probe.style.color = value;
    document.body.append(probe);
    const colour = getComputedStyle(probe).color;
    probe.remove();
    return colour;
  }, surface);

  // The state chip: black on the warning's fill.
  const chip = page.locator('jfb-form-workspace-editor uui-tag').first();
  await expect(chip).toBeVisible();
  const [label, fill] = await chip.evaluate((tag) => {
    const style = getComputedStyle(tag);
    return [style.color, style.backgroundColor];
  });
  expect(contrast(label, fill), "the state chip's label on its fill").toBeGreaterThanOrEqual(4.5);

  // The form check's badge: its icon says the form is clear, beside its words.
  const icon = design.locator('.bo-check uui-icon').first();
  await expect(icon).toBeVisible();
  const iconColour = await icon.evaluate((element) => getComputedStyle(element).color);
  expect(contrast(iconColour, surfaceColour), "the check badge's icon").toBeGreaterThanOrEqual(3);

  // Rings, after a key so that the browser draws them: a palette item, an inspector section's head, the toolbar's
  // widths and UUI's own Close in the inspector.
  await page.keyboard.press('Shift+Tab');
  const places = [
    design.locator('jfb-designer-palette .item'),
    design.locator('jfb-designer-inspector .section-head button'),
    design.locator('jfb-form-canvas .bo-widths button:not([aria-pressed="true"])'),
    design.locator('jfb-designer-inspector .head > uui-button').last(),
  ];
  for (const place of places) {
    await place.filter({ visible: true }).first().focus();
    const { tag, style, colour } = await ring(page);
    expect(style, `the ring of ${tag}`).not.toBe('none');
    expect(contrast(colour, surfaceColour), `the ring of ${tag} on the surface`).toBeGreaterThanOrEqual(3);
  }
});
