import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, formPages, pathOf } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';

// The built-in themes at a phone's width (ROADMAP 6.3a, 6.3c, ADR-0105). The demo shows its forms in a theme of the
// site's through `?theme=`, which it keeps in a cookie, so a post keeps it too. In every theme, `unstyled` included,
// each page with a form fits a 375 px window in English and German without scrolling sideways, with JavaScript and
// without, each form renders in the theme with the functional base rules loaded, and its honeypot is out of view and
// out of the accessibility tree; and a ranking's move without JavaScript opens the page with the option before the
// moved one in view.

test.use({ viewport: { width: 375, height: 812 } });

const THEMES = ['default', 'unstyled', 'bootstrap5'] as const;
const WIDTH = 375;
const HEIGHT = 812;

const pageFormOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** The elements whose box reaches past the window's right edge, by tag, id and class, for a page that scrolls sideways. */
async function widerThanTheWindow(page: Page): Promise<string[]> {
  return page.evaluate(
    (width) =>
      [...document.querySelectorAll('body *')]
        .filter((element) => element.getBoundingClientRect().right > width + 0.5)
        .map(
          (element) => `${element.localName}${element.id ? '#' + element.id : ''}.${[...element.classList].join('.')}`,
        )
        .slice(0, 12),
    WIDTH,
  );
}

/** The checks on one page: the theme, the base rules, no sideways scrolling, and every form's honeypot. */
async function checkPage(page: Page, path: string, theme: string, forms: number): Promise<void> {
  const response = await page.goto(`${path}?theme=${theme}`);
  expect(response?.status(), path).toBe(200);
  const all = page.locator('form.jfb-form');
  await expect(all, `${path}: its form and the newsletter's`).toHaveCount(forms);
  for (const form of await all.all()) {
    await expect(form, `${path}: in ${theme}`).toHaveClass(new RegExp(`(^| )jfb-theme-${theme}( |$)`));
    const honeypot = form.locator('.jfb-hp');
    await expect(honeypot, `${path}: one honeypot per form`).toHaveCount(1);
    const box = await honeypot.boundingBox();
    expect(box, `${path}: the honeypot has a box`).not.toBeNull();
    expect(box!.width * box!.height, `${path}: the honeypot is no bigger than a pixel`).toBeLessThanOrEqual(1);
    expect(
      await honeypot.evaluate((element) => getComputedStyle(element).clipPath),
      `${path}: and clipped to nothing, so it is out of view`,
    ).toBe('inset(50%)');
    const label = (await honeypot.locator('label').textContent())?.trim() ?? '';
    expect(label.length, `${path}: the honeypot has its label`).toBeGreaterThan(0);
    expect(await form.ariaSnapshot(), `${path}: the honeypot is out of the accessibility tree`).not.toContain(label);
    await expect(form.getByRole('textbox', { name: label }), path).toHaveCount(0);
  }

  await expect(
    page.locator('link[rel="stylesheet"][href^="/jfb/assets/themes/base.css?v="]'),
    `${path}: the base rules once`,
  ).toHaveCount(1);
  const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(
    scrollWidth,
    `${path} in ${theme} scrolls sideways: ${(await widerThanTheWindow(page)).join(', ')}`,
  ).toBeLessThanOrEqual(WIDTH);
}

for (const [javaScript, scripts] of [
  [false, 'without JavaScript'],
  [true, 'with JavaScript'],
] as const) {
  test.describe(scripts, () => {
    test.use({ javaScriptEnabled: javaScript });
    for (const theme of THEMES) {
      test(`${theme}: every demo form fits a 375 px window in English and German ${scripts}, with its honeypot out of view and out of the accessibility tree`, async ({
        page,
        memberOf,
      }) => {
        const member = memberOf(0);
        await signInMember(page, member.email, member.password);
        for (const language of LANGUAGES) {
          await checkPage(page, `/${language}/`, theme, 1);
          for (const shown of formPages()) {
            await checkPage(page, pathOf(shown.texts[language], language), theme, 2);
          }
        }
      });
    }
  });
}

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false });
  for (const theme of THEMES) {
    test(`${theme}: a ranking's move without JavaScript opens the page with the option before the moved one in view`, async ({
      page,
    }) => {
      // A site may scroll smoothly to the place (Bootstrap's reboot does, unless the visitor asks for less motion); the
      // place is the same either way, so the check reads it without the animation.
      await page.emulateMedia({ reducedMotion: 'reduce' });
      const feedback = formPages().find((shown) => shown.form === 'feedback');
      expect(feedback, 'the demo has the feedback page').toBeDefined();
      await page.goto(`${pathOf(feedback!.texts.en, 'en')}?theme=${theme}`);
      await expect(pageFormOn(page)).toHaveClass(new RegExp(`(^| )jfb-theme-${theme}( |$)`));
      const loaded = page.waitForEvent('load');
      await pageFormOn(page).getByRole('button', { name: 'Move Faster Wi-Fi up', exact: true }).click({ force: true });
      await loaded;

      expect(new URL(page.url()).hash, "the page opens at the option's new place").toBe('#jfb-1-improve-place-4');
      await expect(pageFormOn(page), 'the theme kept across the post').toHaveClass(
        new RegExp(`(^| )jfb-theme-${theme}( |$)`),
      );
      const moved = await page.locator('#jfb-1-improve-place-4').boundingBox();
      const before = await page.locator('#jfb-1-improve-place-3').boundingBox();
      expect(moved, 'the moved option').not.toBeNull();
      expect(before, 'the option before it').not.toBeNull();
      expect(before!.y, 'the option before the moved one starts in the window').toBeGreaterThanOrEqual(0);
      expect(before!.y + before!.height, 'and ends above the moved one').toBeLessThanOrEqual(moved!.y + 0.5);
      expect(moved!.y + moved!.height, 'the moved option is in the window too').toBeLessThanOrEqual(HEIGHT);
    });
  }
});
