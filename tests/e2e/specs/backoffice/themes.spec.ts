import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Locator, Page, TestInfo } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, unique } from '../../support/forms-api.ts';

// Library > Themes (ROADMAP 12.1, IA 7, ADR-0215): a designer makes a theme on the default theme through the screens.
// The create dialog asks the base theme first; the colours are set with their contrast checks, one failing check fixed
// with the colour it offers, the dark scheme's own colours, the type, spacing, corners and buttons chosen, custom CSS
// with an import the sanitiser takes out; the sample form shows the layer as each change lands, and the saved theme is
// listed and served to forms. Axe checks the editor with its preview beside it and, in a narrow window, by itself.

const SECTION = '/umbraco/section/jfb-forms';
const THEMES = `${SECTION}/workspace/jfb-themes`;

interface Theme {
  readonly key: string;
  readonly alias: string;
  readonly baseTheme: string;
  readonly tokens: { light: Record<string, string>; dark: Record<string, string> };
  readonly customCss: string | null;
}

/** Umbraco's open picker and dialogs. */
const modal = (page: Page) => page.locator('umb-backoffice-modal-container');

async function themeOf(api: APIRequestContext, key: string): Promise<Theme> {
  const answer = await api.get(`${API}/theme/${key}`);
  expect(answer.ok(), await answer.text()).toBe(true);
  return (await answer.json()) as Theme;
}

/** A screenshot kept with the run's results, for the screens' review. */
async function look(page: Page, info: TestInfo, what: string): Promise<void> {
  await page.mouse.move(0, 0);
  const path = info.outputPath(`${what.replaceAll(' ', '-')}.png`);
  await page.screenshot({ path });
  await info.attach(what, { path, contentType: 'image/png' });
}

/**
 * Axe's WCAG 2.2 A and AA rules on the sample form as the frame shows it, its layer and scheme with it, in a page of its
 * own on the site, so its stylesheets load: the backoffice's checks leave the preview's frame alone (ADR-0189).
 */
async function axeOnTheSample(page: Page, view: Locator, scheme: 'light' | 'dark') {
  const html = await view
    .locator(`iframe[data-scheme="${scheme}"]`)
    .evaluate((frame) => (frame as HTMLIFrameElement).contentDocument?.documentElement.outerHTML ?? '');
  const sheet = await page.context().newPage();
  try {
    await sheet.route('**/jfb-theme-sample', (route) =>
      route.fulfill({ body: `<!doctype html>${html}`, contentType: 'text/html; charset=utf-8' }),
    );
    await sheet.goto(new URL('/jfb-theme-sample', page.url()).href);
    await expect(sheet.locator('form.jfb-form')).toHaveCount(1);
    const result = await new AxeBuilder({ page: sheet })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(
      result.violations.map(
        (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
      ),
      `axe on the sample, ${scheme}`,
    ).toEqual([]);
  } finally {
    await sheet.close();
  }
}

/** The value of a token in the first sample form, as its page computes it. */
async function computed(page: Page, scheme: 'light' | 'dark', token: string): Promise<string> {
  const frame = page.frameLocator(`jfb-theme-view iframe[data-scheme="${scheme}"]`);
  return frame
    .locator('form.jfb-form')
    .evaluate((form, name) => getComputedStyle(form).getPropertyValue(name).trim(), token);
}

test('a designer makes a theme through the screens, its colours checked and its layer previewed, saved and served', async ({
  page,
  loginAs,
  apiAs,
}, info) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  const name = unique('Autumn');

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);
  await page.locator('umb-menu-item-layout[label="Themes"], uui-menu-item[label="Themes"]').first().click();
  await expect(page).toHaveURL(new RegExp(`${THEMES}$`));
  await look(page, info, 'the list screen');

  // Create theme asks the base theme first.
  await page.getByRole('button', { name: 'Create theme' }).click();
  const dialog = modal(page).locator('jfb-new-theme-modal');
  await expect(dialog.getByText('Which theme does it build on?')).toBeVisible();
  await look(page, info, 'the create dialog');
  await dialog.locator('umb-ref-item', { hasText: 'Default' }).first().click();
  await expect(page).toHaveURL(/\/workspace\/jfb-theme\/create\/default$/);
  const view = page.locator('jfb-theme-view');
  await expect(view.getByText('Builds on Default')).toBeVisible();
  await expect(view.locator('iframe[data-scheme="light"]')).toBeVisible();
  await expect(view.locator('iframe[data-scheme="dark"]')).toBeVisible();

  const box = page.getByRole('textbox', { name: 'Name of the theme' });
  await box.fill(name);
  await box.press('Tab');

  // A border too light for controls fails its check, and the colour it offers passes.
  const border = view.getByRole('textbox', { name: 'Control borders', exact: true });
  await border.fill('#c9ced6');
  await border.press('Tab');
  await expect(view.getByText(/1\.5:1 on the surface: controls need 3:1\./)).toBeVisible();
  await look(page, info, 'a failing check');
  await view.getByRole('button', { name: /^Use #/ }).click();
  await expect(border).toHaveValue(/^#[0-9a-f]{6}$/);
  await expect(view.locator('#colour-light-jfb-color-border-check')).toContainText('Contrast ');
  const accent = view.getByRole('textbox', { name: 'Accent', exact: true });
  await accent.fill('#8a1538');
  await accent.press('Tab');
  await expect.poll(() => computed(page, 'light', '--jfb-color-accent')).toBe('#8a1538');

  // The dark scheme's own accent.
  await view.getByRole('button', { name: 'Dark', exact: true }).click();
  const dark = view.getByRole('textbox', { name: 'Accent', exact: true });
  await expect(dark).toHaveAttribute('placeholder', '#5fd0b8');
  await dark.fill('#f0a5b8');
  await dark.press('Tab');
  await expect.poll(() => computed(page, 'dark', '--jfb-color-accent')).toBe('#f0a5b8');
  await look(page, info, 'the dark colours');

  // Type, spacing and corners, and outlined buttons.
  await view.getByRole('button', { name: 'Type' }).click();
  await view.getByLabel('Font').selectOption('serif');
  await view.getByRole('button', { name: 'Spacing and corners' }).click();
  await view.getByLabel('Spacing').selectOption('roomy');
  await view.getByLabel('Corners').selectOption('6');
  await view.getByRole('button', { name: 'Buttons and focus' }).click();
  await view.getByLabel('Primary button').selectOption('outlined');
  await expect.poll(() => computed(page, 'light', '--jfb-radius')).toBe('6px');
  await look(page, info, 'the sections open');

  // Custom CSS: an import is taken out and said.
  await view.getByRole('button', { name: 'More options' }).click();
  await view
    .getByLabel('Custom CSS')
    .fill('@import url(https://fonts.example.com/a.css);\n.jfb-label { letter-spacing: .02em; }');
  await expect(view.getByText('Taken out of the custom CSS')).toBeVisible();
  await expect(view.getByText('An @import')).toBeVisible();

  await axeInside(page, 'jfb-theme-view');
  await axeOnTheSample(page, view, 'light');
  await axeOnTheSample(page, view, 'dark');
  await look(page, info, 'the editor');

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-theme\/edit\/[0-9a-f-]{36}/);
  const key = /\/edit\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? '';
  await expect(page.locator('jfb-library-item-editor').getByText('Not used by any form')).toBeVisible();
  const saved = await themeOf(api, key);
  expect(saved.baseTheme).toBe('default');
  expect(saved.tokens.light['--jfb-color-accent']).toBe('#8a1538');
  expect(saved.tokens.dark['--jfb-color-accent']).toBe('#f0a5b8');
  expect(saved.tokens.light['--jfb-radius']).toBe('6px');
  expect(saved.tokens.light['--jfb-color-button']).toBe('transparent');
  expect(saved.customCss).toContain('@import');

  // The layer the website serves, under the theme's version.
  const layer = await page.request.get(`/jfb/assets/themes/library/${saved.alias}.css`);
  expect(layer.ok()).toBe(true);
  const css = await layer.text();
  expect(css).toContain(`.jfb-form.jfb-theme-${saved.alias}`);
  expect(css).not.toContain('@import');

  // Narrow: one side at a time, by the switch.
  await page.setViewportSize({ width: 1024, height: 768 });
  await expect(view.getByRole('button', { name: 'Preview', exact: true })).toBeVisible();
  await axeInside(page, 'jfb-theme-view');
  await look(page, info, 'the editor in a narrow window');
  await view.getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(view.locator('iframe[data-scheme="light"]')).toBeVisible();
  await look(page, info, 'the preview in a narrow window');
  await page.setViewportSize({ width: 1440, height: 900 });

  // Listed with its swatch.
  await page.goto(THEMES);
  await expect(page.getByRole('link', { name })).toBeVisible();
  await look(page, info, 'the list with the theme');
});
