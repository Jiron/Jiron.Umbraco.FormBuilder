import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { inLanguage } from '../../support/backoffice-look.ts';
import { API, contactForm, created, unique } from '../../support/forms-api.ts';
import { splitAxe } from '../../support/ux-walk.ts';

// The section at its narrowest (ROADMAP 9.6a, ADR-0182). Umbraco's backoffice keeps a width of its own below a window
// of that width, and its 300 px tree beside the workspace, so on a phone or a tablet every section scrolls sideways as
// one page. The section adds no width to Umbraco's, and inside the workspace Umbraco leaves it nothing of its own is
// cut short or scrolls sideways: measured in German, whose words are the longest, on the Design view with a field
// selected, whose inspector then lies over the canvas with the element's tools in its header, on a folder's list, and
// in the template gallery, whose footer keeps its two buttons side by side inside the window.

/** What the page measures: its width, and inside the section's own elements the cut texts and sideways scrolls. */
const measure = (page: Page) =>
  page.evaluate(() => {
    const ours: Element[] = [];
    const collect = (root: Document | ShadowRoot, inside: boolean) => {
      for (const element of root.querySelectorAll('*')) {
        const mine = inside || element.tagName.toLowerCase().startsWith('jfb-');
        if (mine) ours.push(element);
        if (element.shadowRoot) collect(element.shadowRoot, mine);
      }
    };
    collect(document, false);
    const cut: string[] = [];
    const scrolls: string[] = [];
    for (const element of ours) {
      if (!(element instanceof HTMLElement) || element.scrollWidth <= element.clientWidth + 1) continue;
      const box = element.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) continue;
      const style = getComputedStyle(element);
      const text = (element.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 50);
      if (style.textOverflow === 'ellipsis' && text) cut.push(text);
      else if (style.overflowX === 'auto' || style.overflowX === 'scroll') scrolls.push(element.tagName.toLowerCase());
    }
    return { width: document.documentElement.scrollWidth, cut, scrolls };
  });

for (const window of [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
]) {
  test(`at ${window.width} px the section is as wide as Umbraco's own, and nothing of its own is cut, in German`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const api = await apiAs('designer');
    const folderName = unique('Schmaler Ordner');
    const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);
    const key = await contactForm(api, unique('Schmal'), folder);
    const emptyName = unique('Leerer Ordner');
    const empty = await created(await api.post(`${API}/folder`, { data: { name: emptyName } }), emptyName);
    await inLanguage(page, 'de-DE');
    await page.setViewportSize(window);
    await loginAs('designer');

    // Umbraco's own section sets the width every section has at this window.
    await page.goto('/umbraco/section/content');
    await expect(page.locator('umb-section-sidebar')).toBeVisible();
    const umbraco = await page.evaluate(() => document.documentElement.scrollWidth);

    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`);
    const design = page.locator('jfb-form-design-view');
    await design.locator('jfb-canvas-field [data-control="select"]').first().click();
    const inspector = design.locator('jfb-designer-inspector');
    await expect(inspector).toHaveAttribute('mode', 'overlay');
    await expect(inspector.locator('h2')).toBeVisible();

    const found = await measure(page);
    expect(found.width, "the page's width, as Umbraco's own section has it").toBe(umbraco);
    expect(found.cut, 'texts cut short inside the section').toEqual([]);
    expect(found.scrolls, 'boxes that scroll sideways inside the section').toEqual([]);

    // Nothing of the section lies over Umbraco's header, its back button and the form's name: axe finds no target of
    // the section's too small to hit, as it did while the skip link waited above the view (ADR-0182).
    const targets = await new AxeBuilder({ page }).options({ ancestry: true }).withRules(['target-size']).analyze();
    const small = splitAxe(
      targets.violations.flatMap((violation) =>
        violation.nodes.map((node) => ({
          rule: violation.id,
          target: node.target,
          ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
        })),
      ),
    ).ours;
    expect(small, 'targets too small to hit inside the section').toEqual([]);

    // Every tool of the element lies inside the inspector, its "..." too; the menu it opens is hidden till then.
    const frame = await inspector.boundingBox();
    const tools = await inspector.locator('.tools > button').evaluateAll((buttons) =>
      buttons.map((button) => {
        const box = button.getBoundingClientRect();
        return {
          left: box.left,
          right: box.right,
          name: button.getAttribute('aria-label') ?? button.textContent?.trim(),
        };
      }),
    );
    expect(tools.length).toBeGreaterThanOrEqual(4);
    for (const tool of tools) {
      expect(tool.left, `${tool.name}: its left edge`).toBeGreaterThanOrEqual((frame?.x ?? 0) - 0.5);
      expect(tool.right, `${tool.name}: its right edge`).toBeLessThanOrEqual(
        (frame?.x ?? 0) + (frame?.width ?? 0) + 0.5,
      );
    }

    // A folder's list: its columns as its own width allows, nothing cut.
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-folder/edit/${folder}`);
    await expect(page.locator('jfb-folder-list table')).toBeVisible();
    const list = await measure(page);
    expect([list.width, list.cut, list.scrolls], "a folder's list").toEqual([umbraco, [], []]);

    // The template gallery: the footer's two buttons in one row, inside the window.
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-folder/edit/${empty}`);
    await page.locator('jfb-folder-list uui-button[look="outline"]').click();
    const gallery = page.locator('jfb-template-gallery-modal');
    await expect(gallery.locator('[role="radio"]').first()).toBeVisible();
    const footerOf = () =>
      gallery.locator('[slot="actions"] uui-button').evaluateAll((buttons) =>
        buttons.map((button) => {
          const box = button.getBoundingClientRect();
          return { top: Math.round(box.top), right: box.right };
        }),
      );
    // Umbraco's sidebar slides in from the window's edge, so its footer is measured where it comes to rest.
    await expect
      .poll(async () => Math.max(...(await footerOf()).map((button) => button.right)), { message: 'the right edge' })
      .toBeLessThanOrEqual(window.width);
    const footer = await footerOf();
    expect(footer).toHaveLength(2);
    expect(footer[0]?.top, 'one row').toBe(footer[1]?.top);
    const shown = await measure(page);
    expect([shown.cut, shown.scrolls], 'the gallery').toEqual([[], []]);
  });
}
