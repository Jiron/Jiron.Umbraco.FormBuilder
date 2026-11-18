import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { openSearch } from '../../support/search.ts';

// The Forms section's shell (ROADMAP 9.1, ADR-0021, ADR-0165): the checklist's form block step turns green, and the
// Overview holds up in Umbraco's three themes. What each persona sees on the section's left side is places.spec.ts's.
// A confirm that shows a name as text is the library's (library.spec.ts), whose Recycle Bin asks before deleting for
// good.

const OVERVIEW = '/umbraco/section/jfb-forms';
const API = '/umbraco/jfb/management/api/v1';

test("the checklist's button for the form block calls setup/form-block, and its item turns green", async ({
  page,
  loginAs,
}) => {
  // The host is shared and another spec may have made the types: until the button is pressed, the step reads as on a site without
  // them; afterwards it reads as the server says. The other steps stand for what this test does not look at.
  let pressed = false;
  await page.route(`**${API}/setup/checklist`, async (route) => {
    try {
      const response = await route.fetch();
      const body = (await response.json()) as { complete: boolean; items: { step: string; done: boolean }[] };
      body.complete = false;
      for (const item of body.items) item.done = item.step === 'formBlock' ? pressed && item.done : true;
      await route.fulfill({ response, json: body });
    } catch (error) {
      // A request the page gave up on, as a navigation does, is answered by nobody: its response is gone.
      if (!/disposed|closed/i.test(String(error))) throw error;
    }
  });

  await loginAs('admin');
  await page.goto(OVERVIEW);
  const step = page.locator('jfb-overview ol li').nth(3);
  await expect(step).toContainText('Put the form on a page');
  await expect(step.locator('.mark--open')).toBeVisible();

  await step.getByRole('button', { name: 'Create a form block type\u2026' }).click();
  const dialog = page.locator('jfb-form-block-modal');
  await expect(dialog).toContainText('Types that exist already stay as they are.');
  const creating = page.waitForResponse(`**${API}/setup/form-block`);
  pressed = true;
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  expect((await creating).status()).toBe(200);
  await expect(dialog).toContainText('One step is left');
  await expect(dialog.getByRole('link', { name: 'Open data types' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Close' }).click();

  await expect(page.locator('jfb-overview ol li').nth(3).locator('.mark--done')).toBeVisible();
  await expect(page.locator('jfb-overview .count')).toHaveText('5 of 5 done');
});

test('the header search finds a form and links to it', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const name = `Searched ${Date.now()}`;
  const created = await api.post(`${API}/form`, { data: { name } });
  expect(created.status(), await created.text()).toBe(201);
  // The answer holds the new form; there is no Location header to read its key from.
  const { key } = (await created.json()) as { key: string };
  expect(key).toMatch(/^[0-9a-f-]{36}$/);

  await loginAs('designer');
  await page.goto(OVERVIEW);
  await openSearch(page);
  await page.locator('umb-search-modal input').fill(name);
  const result = page.locator('umb-search-modal a', { hasText: name });
  await expect(result).toHaveAttribute('href', new RegExp(`section/jfb-forms/workspace/jfb-form/edit/${key}$`));
});

/** A colour as the browser computes it, as its red, green and blue from 0 to 1. */
function channelsOf(color: string): [number, number, number] {
  const srgb = /color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/.exec(color);
  if (srgb) return [Number(srgb[1]), Number(srgb[2]), Number(srgb[3])];
  const rgb = /rgba?\((\d+), (\d+), (\d+)/.exec(color);
  if (rgb) return [Number(rgb[1]) / 255, Number(rgb[2]) / 255, Number(rgb[3]) / 255];
  throw new Error(`A colour that can't be read: ${color}`);
}

function contrast(one: string, other: string): number {
  const luminance = (color: string) => {
    const [red, green, blue] = channelsOf(color).map((value) =>
      value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4,
    ) as [number, number, number];
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  };
  const [light, dark] = [luminance(one), luminance(other)].sort((a, b) => b - a) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

for (const [theme, alias] of [
  ['light', 'umb-light-theme'],
  ['dark', 'umb-dark-theme'],
  ['high-contrast', 'umb-high-contrast-theme'],
] as const) {
  test(`in the ${theme} theme, axe is clean on the Overview and its own colours keep their contrast`, async ({
    page,
    loginAs,
  }) => {
    await page.addInitScript((value) => localStorage.setItem('umb-theme-alias', value), alias);
    await loginAs('admin');
    await page.goto(OVERVIEW);
    const overview = page.locator('jfb-overview');
    await expect(overview.locator('uui-box').first()).toBeVisible();
    await expect(overview.locator('[aria-busy="true"]')).toHaveCount(0);

    // Umbraco's own elements are its to answer for, so what counts is what lies inside the Overview.
    const result = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .options({ ancestry: true })
      .analyze();
    const ours = result.violations.flatMap((violation) =>
      violation.nodes
        .filter((node) => JSON.stringify(node.ancestry).includes('jfb-overview'))
        .map((node) => `${violation.id}: ${JSON.stringify(node.target)}`),
    );
    expect(ours).toEqual([]);

    // Each token as the theme gives it, against each ground the backoffice draws on (docs/ux/tokens.md).
    const colours = await overview.evaluate((host) => {
      const read = (property: string, name: string) => {
        const probe = document.createElement('span');
        probe.style.setProperty(property, `var(${name})`);
        host.shadowRoot?.append(probe);
        const value = getComputedStyle(probe).getPropertyValue(property);
        probe.remove();
        return value;
      };
      return {
        muted: read('color', '--jfb-bo-color-text-muted'),
        border: read('color', '--jfb-bo-color-control-border'),
        surface: read('background-color', '--uui-color-surface'),
        background: read('background-color', '--uui-color-background'),
      };
    });
    expect(contrast(colours.muted, colours.surface), 'muted text on the surface').toBeGreaterThanOrEqual(4.5);
    expect(contrast(colours.muted, colours.background), 'muted text on the background').toBeGreaterThanOrEqual(4.5);
    expect(contrast(colours.border, colours.surface), 'a control border on the surface').toBeGreaterThanOrEqual(3);
    expect(contrast(colours.border, colours.background), 'a control border on the background').toBeGreaterThanOrEqual(
      3,
    );
  });
}
